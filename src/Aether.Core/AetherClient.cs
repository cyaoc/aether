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

    // BilibiliApi keeps cookies per flow so a fresh login cannot inherit another flow's credential.
    public AetherClient(TimeProvider timeProvider, ILogger<AetherClient> logger)
        : this(new HttpClientHandler { UseCookies = false }, ConnectWebSocketAsync, timeProvider, logger,
            LocateDataDirectory()) { }

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

    /// <summary>Retries network failures (not B站 error codes) until the request succeeds or the caller cancels.</summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken)
    {
        while (true)
        {
            try { return await request(); }
            catch (Exception error) when (error is HttpRequestException
                || (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
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
        cancellationToken.ThrowIfCancellationRequested();
        var credentials = new CredentialStore(new Database(dataDirectory));
        var api = new BilibiliApi(http, timeProvider);
        var credential = credentials.Load();
        if (credential is not null) api.AddCookies(credential.Cookies);
        var navigation = await api.GetNavigationAsync(cancellationToken);
        if (credential is null || navigation.Mid == 0)
        {
            await foreach (var update in LoginAsync(cancellationToken))
                if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
            cancellationToken.ThrowIfCancellationRequested();
            api = new BilibiliApi(http, timeProvider);
            api.AddCookies(credentials.Load()!.Cookies);
            navigation = await api.GetNavigationAsync(cancellationToken);
            if (navigation.Mid == 0) throw new InvalidOperationException("扫码后登录凭据未生效，请重新扫码登录。");
        }
        yield return new Connecting();
        var connection = await api.GetConnectionAsync(roomId, navigation.MixinKey, cancellationToken);
        WebSocket socket;
        try { socket = await connectWebSocket(connection.Server, cancellationToken); }
        catch (WebSocketException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WebSocketException(error.WebSocketErrorCode,
                $"连接弹幕服务器 {connection.Server.Host}:{connection.Server.Port} 失败：{error.GetBaseException().Message}", error);
        }
        using var ownedSocket = socket;
        var authentication = JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = navigation.Mid, roomid = connection.RoomId, protover = 3, platform = "web", type = 2,
            key = connection.Token, buvid = connection.Buvid
        });
        await socket.SendAsync(DanmakuProtocol.Pack(7, authentication), WebSocketMessageType.Binary, true, cancellationToken);
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = Task.CompletedTask;
        try
        {
            var connected = false;
            while (true)
            {
                var bytes = await ReceiveMessageAsync(socket, connectionStop.Token);
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
                        using var message = JsonDocument.Parse(packet.Body);
                        var root = message.RootElement;
                        var command = root.GetProperty("cmd").GetString()!;
                        if (command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal))
                        {
                            var info = root.GetProperty("info");
                            yield return new Danmaku(receivedAt, info[2][1].GetString()!, info[1].GetString()!);
                        }
                        else logger.LogDebug("忽略直播间事件 {Command}", command);
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

    private static async Task<byte[]> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("弹幕服务器已断开连接。");
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

    /// <summary>ADR 0002: Release uses the executable's directory; Debug prefers the directory holding Aether.slnx.</summary>
    private static string LocateDataDirectory()
    {
        var root = AppContext.BaseDirectory;
#if DEBUG
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "Aether.slnx"))) continue;
            root = directory.FullName;
            break;
        }
#endif
        return Path.Combine(root, "data");
    }

    public void Dispose() => http.Dispose();
}
