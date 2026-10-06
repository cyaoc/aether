using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
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
    private readonly Lazy<CredentialStore> credentialStore = new(() => new CredentialStore(new Database(dataDirectory)));

    // No data at all for this long, not even a heartbeat reply, means the room connection is dead.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    // BilibiliApi keeps cookies per flow so a fresh login cannot inherit another flow's credential.
    public AetherClient(ILogger<AetherClient> logger, string dataDirectory)
        : this(new HttpClientHandler { UseCookies = false }, ConnectWebSocketAsync, TimeProvider.System, logger,
            dataDirectory) { }

    public IAsyncEnumerable<LoginUpdate> LoginAsync(CancellationToken cancellationToken = default) =>
        EndOnCancellation(LoginCoreAsync(cancellationToken), cancellationToken);

    /// <summary>Signs out on B站 when possible; the local credential is deleted either way unless the caller cancels.</summary>
    /// <remarks>Disconnect any room connection before signing out, and do not start a new one until sign-out completes.
    /// CLI and GUI may share the data directory across processes; this ordering is the caller's responsibility.</remarks>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var credentials = credentialStore.Value;
        try
        {
            if (credentials.Load() is { } credential)
            {
                var api = new BilibiliApi(http, timeProvider, credential);
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
        return EndOnCancellation(WatchWithReconnectAsync(roomId, cancellationToken), cancellationToken);
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
        var credentials = credentialStore.Value;
        var api = new BilibiliApi(http, timeProvider, credential: null);
        while (true)
        {
            var qr = await RetryAsync(() => api.GenerateQrCodeAsync(cancellationToken), cancellationToken);
            yield return new LoginQrCode(qr.Url);
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
                var (state, credential) = await RetryAsync(() => api.PollQrCodeAsync(qr.Key, cancellationToken), cancellationToken);
                if (state == QrCodeState.Waiting) continue;
                if (state == QrCodeState.Expired) break;
                cancellationToken.ThrowIfCancellationRequested();
                credentials.Save(credential!);
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

    private async IAsyncEnumerable<WatchUpdate> WatchWithReconnectAsync(
        long roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reconnecting = false;
        // Past the first credential check, a credential that expired or was deleted reconnects
        // anonymously instead of asking for a QR scan in the middle of the room connection.
        var needsInitialLogin = true;
        var retrySeconds = 1;
        while (true)
        {
            await using (var updates = WatchAttemptAsync().GetAsyncEnumerator(cancellationToken))
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

        async IAsyncEnumerable<WatchUpdate> WatchAttemptAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var credentials = credentialStore.Value;
            var api = new BilibiliApi(http, timeProvider, credentials.Load());
            if (!await api.IsLoggedInAsync(cancellationToken))
            {
                if (needsInitialLogin)
                {
                    await foreach (var update in LoginCoreAsync(cancellationToken))
                        if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
                    api = new BilibiliApi(http, timeProvider, credentials.Load());
                    if (!await api.IsLoggedInAsync(cancellationToken))
                        throw new InvalidOperationException("扫码后登录凭据未生效，请重新扫码登录。");
                }
                else logger.LogWarning("登录凭据已失效，以匿名身份重连，观众昵称可能被打码。");
            }
            needsInitialLogin = false;
            yield return new Connecting();
            await foreach (var update in ReceiveRoomUpdatesAsync(roomId, api, cancellationToken))
                yield return update;
        }
    }

    private async IAsyncEnumerable<WatchUpdate> ReceiveRoomUpdatesAsync(
        long roomId, BilibiliApi api, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await api.GetConnectionAsync(roomId, cancellationToken);
        using var socket = await connectWebSocket(connection.Server, cancellationToken);
        var authentication = DanmakuProtocol.CreateAuthentication(connection.Mid, connection.RoomId, connection.Token, connection.Buvid);
        using var idleTimeout = new CancellationTokenSource(IdleTimeout, timeProvider);
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleTimeout.Token);
        await socket.SendAsync(authentication, WebSocketMessageType.Binary, true, connectionStop.Token);
        Task heartbeat = Task.CompletedTask;
        try
        {
            var connected = false;
            while (true)
            {
                var bytes = await ReceiveMessageAsync(socket, idleTimeout, connectionStop.Token);
                var receivedAt = timeProvider.GetLocalNow();
                foreach (var decoded in DanmakuProtocol.Decode(bytes, receivedAt, connected, logger))
                {
                    if (decoded is DanmakuProtocol.AuthenticationReply auth)
                    {
                        if (!auth.Success)
                            throw new InvalidOperationException("弹幕服务器认证失败。");
                        if (!connected)
                        {
                            connected = true;
                            heartbeat = SendHeartbeatsAsync(socket, connectionStop);
                            yield return new Connected();
                        }
                    }
                    else if (decoded is DanmakuProtocol.DanmakuReceived danmaku)
                        yield return danmaku.Danmaku;
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

    private async Task SendHeartbeatsAsync(WebSocket socket, CancellationTokenSource stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
                await socket.SendAsync(DanmakuProtocol.CreateHeartbeat(), WebSocketMessageType.Binary, true, stop.Token);
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
