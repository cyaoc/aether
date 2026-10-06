using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Owns the supplied HTTP handler and each WebSocket returned by the connection delegate.</summary>
public sealed class AetherClient(
    HttpMessageHandler httpHandler,
    Func<Uri, CancellationToken, Task<WebSocket>> connectWebSocket,
    TimeProvider timeProvider,
    ILogger<AetherClient> logger,
    string dataDirectory) : IDisposable
{
    private readonly HttpClient http = new(httpHandler);

    // No data at all for this long, not even a heartbeat reply, means the room connection is dead.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    // BilibiliApi keeps cookies per flow so a fresh login cannot inherit another flow's credential.
    public AetherClient(ILogger<AetherClient> logger, string dataDirectory)
        : this(new HttpClientHandler { UseCookies = false }, ConnectWebSocketAsync, TimeProvider.System, logger,
            dataDirectory) { }

    public IAsyncEnumerable<LoginUpdate> LoginAsync(CancellationToken cancellationToken = default) =>
        EndOnCancellation(LoginCoreAsync(cancellationToken), cancellationToken);

    /// <summary>Signs out on B站 when possible; the local credential is deleted either way unless the caller cancels.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var credentials = new CredentialStore(new Database(dataDirectory));
        try
        {
            if (credentials.Load() is { } credential)
            {
                var api = new BilibiliApi(http, timeProvider);
                api.AddCookies(credential.Cookies);
                await api.LogoutAsync(cancellationToken);
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("B站退出登录失败，仍删除本地登录凭据：{Error}", error.Message);
        }
        credentials.Delete();
    }

    public IAsyncEnumerable<WatchUpdate> WatchAsync(long roomId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        return EndOnCancellation(WatchCoreAsync(roomId, cancellationToken), cancellationToken);
    }

    /// <summary>Ends the stream normally, instead of throwing, once the caller cancels.</summary>
    private static async IAsyncEnumerable<T> EndOnCancellation<T>(
        IAsyncEnumerable<T> source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var updates = source.GetAsyncEnumerator(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasNext;
            try { hasNext = await updates.MoveNextAsync(); }
            catch (Exception error) when (cancellationToken.IsCancellationRequested
                && error is OperationCanceledException or WebSocketException) { yield break; }
            if (!hasNext) yield break;
            yield return updates.Current;
        }
    }

    private async IAsyncEnumerable<LoginUpdate> LoginCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Everything that can fail on its own runs before the first QR code, so no scan is wasted.
        var credentials = new CredentialStore(new Database(dataDirectory));
        var api = new BilibiliApi(http, timeProvider);
        await RetryAsync(() => api.GetBuvidAsync(cancellationToken), cancellationToken);
        while (true)
        {
            var qr = await RetryAsync(() => api.GenerateQrCodeAsync(cancellationToken), cancellationToken);
            yield return new LoginQrCode(qr.Url);
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
                var (state, refreshToken) = await RetryAsync(() => api.PollQrCodeAsync(qr.Key, cancellationToken), cancellationToken);
                if (state == QrCodeState.Waiting) continue;
                if (state == QrCodeState.Expired) break;
                cancellationToken.ThrowIfCancellationRequested();
                credentials.Save(new Credential(api.GetCookies(), refreshToken!, timeProvider.GetUtcNow()));
                yield return new LoggedIn();
                yield break;
            }
        }
    }

    /// <summary>Only a failed connection is worth retrying. Once B站 answers with a refusal (an error code, HTTP 4xx, rejected
    /// authentication) the same request gets the same answer and hammering it deepens risk control; local errors and bugs never heal.</summary>
    private static bool IsTransient(Exception error) => error
        is HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError }
        or WebSocketException or OperationCanceledException;

    /// <summary>Retries <see cref="IsTransient"/> failures until the request succeeds or the caller cancels.</summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken)
    {
        while (true)
        {
            try { return await request(); }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsTransient(error))
            {
                // ponytail: fixed 2s retry with no cap; add backoff if B站 starts rate-limiting retries.
                logger.LogWarning("登录请求失败，2 秒后重试：{Error}", error.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
            }
        }
    }

    private async IAsyncEnumerable<WatchUpdate> WatchCoreAsync(
        long roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reconnecting = false;
        // Past the first credential check, a credential that expired or was deleted reconnects
        // anonymously instead of asking for a QR scan in the middle of the room connection.
        var allowAnonymous = false;
        var retrySeconds = 1;
        while (true)
        {
            await using (var updates = WatchAttemptAsync(roomId, allowAnonymous, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    bool hasNext;
                    try { hasNext = await updates.MoveNextAsync(); }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsTransient(error))
                    {
                        logger.LogWarning("直播间连接中断，{Seconds} 秒后重试：{Error}", retrySeconds, error.Message);
                        break;
                    }
                    if (!hasNext) yield break;
                    if (updates.Current is Connecting) allowAnonymous = true;
                    if (reconnecting && updates.Current is Connecting) continue;
                    if (updates.Current is Connected) retrySeconds = 1;
                    yield return updates.Current;
                }
            }
            reconnecting = true;
            yield return new Reconnecting();
            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), timeProvider, cancellationToken);
            retrySeconds = Math.Min(retrySeconds * 2, 30);
        }
    }

    private async IAsyncEnumerable<WatchUpdate> WatchAttemptAsync(
        long roomId, bool allowAnonymous, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var credentials = new CredentialStore(new Database(dataDirectory));
        var (api, mid, mixinKey) = await CheckCredentialAsync(credentials.Load(), cancellationToken, allowAnonymous);
        if (mid == 0 && allowAnonymous) logger.LogWarning("登录凭据已失效，以匿名身份重连，观众昵称可能被打码。");
        else if (mid == 0)
        {
            await foreach (var update in LoginCoreAsync(cancellationToken))
                if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
            (api, mid, mixinKey) = await CheckCredentialAsync(credentials.Load(), cancellationToken);
            if (mid == 0) throw new InvalidOperationException("扫码后登录凭据未生效，请重新扫码登录。");
        }
        yield return new Connecting();
        var connection = await api.GetConnectionAsync(roomId, mixinKey, cancellationToken);
        using var socket = await connectWebSocket(connection.Server, cancellationToken);
        var authentication = JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = mid, roomid = connection.RoomId, protover = 3, platform = "web", type = 2,
            key = connection.Token, buvid = connection.Buvid
        });
        using var idleTimeout = new CancellationTokenSource(IdleTimeout, timeProvider);
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleTimeout.Token);
        await socket.SendAsync(DanmakuProtocol.Pack(7, authentication), WebSocketMessageType.Binary, true, connectionStop.Token);
        Task heartbeat = Task.CompletedTask;
        try
        {
            var connected = false;
            while (true)
            {
                var bytes = await ReceiveMessageAsync(socket, idleTimeout, connectionStop.Token);
                var receivedAt = timeProvider.GetLocalNow();
                foreach (var packet in DanmakuProtocol.Unpack(bytes))
                {
                    if (packet.Operation == 8)
                    {
                        using var auth = JsonDocument.Parse(packet.Body);
                        if (auth.RootElement.GetProperty("code").GetInt32() != 0)
                            throw new InvalidOperationException("弹幕服务器认证失败。");
                        if (!connected)
                        {
                            connected = true;
                            heartbeat = SendHeartbeatsAsync(socket, connectionStop);
                            yield return new Connected();
                        }
                    }
                    else if (packet.Operation == 5 && connected)
                    {
                        if (ParseRoomMessage(packet.Body, receivedAt) is { } danmaku)
                            yield return danmaku;
                    }
                }
            }
        }
        finally
        {
            await connectionStop.CancelAsync();
            try { await heartbeat; }
            finally
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1), timeProvider);
                    try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closeTimeout.Token); }
                    catch (Exception error) when (error is WebSocketException or OperationCanceledException)
                    { logger.LogDebug(error, "关闭弹幕连接时传输已不可用"); }
                }
            }
        }
    }

    private Danmaku? ParseRoomMessage(ReadOnlyMemory<byte> body, DateTimeOffset receivedAt)
    {
        string? command = null;
        try
        {
            using var message = JsonDocument.Parse(body);
            var root = message.RootElement;
            command = root.GetProperty("cmd").GetString() ?? throw new JsonException("缺少 cmd。");
            if (command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal))
            {
                var info = root.GetProperty("info");
                var nickname = info[2][1].GetString() ?? throw new JsonException("缺少弹幕昵称。");
                var content = info[1].GetString() ?? throw new JsonException("缺少弹幕内容。");
                return new Danmaku(receivedAt, nickname, content);
            }
        }
        // Only JSON parsing and field access are inside this boundary; frame errors still end the stream.
        catch (Exception error) when (error is JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException)
        {
            logger.LogWarning("跳过无法解析的直播间消息（cmd: {Command}）：{Error}", command, error.Message);
            return null;
        }
        logger.LogDebug("忽略直播间事件 {Command}", command);
        return null;
    }

    /// <summary>Asks nav whether the credential still logs in; mid 0 means there is none or B站 rejected it.
    /// With <paramref name="allowAnonymous"/>, mid 0 still comes with a wbi key and buvid3 for an anonymous connection.</summary>
    private async Task<(BilibiliApi Api, long Mid, string MixinKey)> CheckCredentialAsync(
        Credential? credential, CancellationToken cancellationToken, bool allowAnonymous = false)
    {
        var api = new BilibiliApi(http, timeProvider);
        if (credential is not null) api.AddCookies(credential.Cookies);
        else if (allowAnonymous) await api.GetBuvidAsync(cancellationToken);
        else return (api, 0, "");
        var (mid, mixinKey) = await api.GetNavigationAsync(cancellationToken, allowAnonymous);
        return (api, mid, mixinKey);
    }

    private async Task SendHeartbeatsAsync(WebSocket socket, CancellationTokenSource stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
                await socket.SendAsync(DanmakuProtocol.Pack(2, []), WebSocketMessageType.Binary, true, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch
        {
            await stop.CancelAsync(); // Wake the receive loop so a send failure cannot leave WatchAsync hanging.
            throw;
        }
    }

    private static async Task<byte[]> ReceiveMessageAsync(
        WebSocket socket, CancellationTokenSource idleTimeout, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.Count > 0) idleTimeout.CancelAfter(IdleTimeout);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("弹幕服务器已断开连接。");
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("弹幕服务器返回了非二进制消息。");
            if (message.Length + result.Count > DanmakuProtocol.MaxPacketSize)
                throw new InvalidDataException("弹幕数据包过大。");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return message.ToArray();
    }

    private static async Task<WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0");
        socket.Options.SetRequestHeader("Origin", "https://live.bilibili.com");
        try { await socket.ConnectAsync(uri, cancellationToken); return socket; }
        catch { socket.Dispose(); throw; }
    }

    public void Dispose() => http.Dispose();
}
