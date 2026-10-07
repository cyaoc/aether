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
    // PublicationOnly does not cache a failed open, so a transient database error does not poison a long-lived client.
    private readonly Lazy<CredentialStore> credentialStore = new(
        () => new CredentialStore(new Database(dataDirectory)), LazyThreadSafetyMode.PublicationOnly);

    // No data at all for this long, not even a heartbeat reply, means the room connection is dead.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CredentialCheckInterval = TimeSpan.FromHours(24);

    // BilibiliApi keeps cookies per flow so a fresh login cannot inherit another flow's credential.
    public AetherClient(ILogger<AetherClient> logger, string dataDirectory)
        : this(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All },
            ConnectWebSocketAsync, TimeProvider.System, logger,
            dataDirectory) { }

    public IAsyncEnumerable<LoginUpdate> LoginAsync(CancellationToken cancellationToken = default) =>
        EndOnCancellation(LoginCoreAsync(cancellationToken), cancellationToken);

    /// <summary>Signs out on B站 when possible; the local credential is deleted either way unless the caller cancels.</summary>
    /// <remarks>Takes no room lock. A room connection running elsewhere on this data directory keeps going,
    /// and its next reconnect asks for a scan.</remarks>
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
        [EnumeratorCancellation] CancellationToken cancellationToken, bool acceptExternalCredential = false,
        string? previousToken = null)
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
                if (acceptExternalCredential && credentials.Load() is { } external && external.RefreshToken != previousToken)
                {
                    yield return new LoggedIn(); // The watch flow validates it with nav; do not save over another process.
                    yield break;
                }
                QrCodeState state;
                Credential? credential;
                try { (state, credential) = await api.PollQrCodeAsync(qr.Key, cancellationToken); }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsTransient(error))
                {
                    logger.LogWarning("登录请求失败，2 秒后重试：{Error}", error.Message);
                    continue;
                }
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
        long? realRoomId = null;
        var reconnecting = false;
        var retrySeconds = 1;
        FileStream? roomLock = null;
        try
        {
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
        }
        finally { roomLock?.Dispose(); }

        async IAsyncEnumerable<WatchUpdate> WatchAttemptAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            realRoomId ??= await new BilibiliApi(http, timeProvider, credential: null)
                .ResolveRoomIdAsync(roomId, cancellationToken);
            roomLock ??= AcquireRoomLock(realRoomId.Value);
            var credentials = credentialStore.Value;
            var credential = await RefreshCredentialIfDueAsync(cancellationToken, cancellationToken);
            var api = new BilibiliApi(http, timeProvider, credential);
            while (!await api.IsLoggedInAsync(cancellationToken))
            {
                await foreach (var update in LoginCoreAsync(cancellationToken,
                    acceptExternalCredential: true, previousToken: credential?.RefreshToken))
                    if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
                credential = credentials.Load();
                api = new BilibiliApi(http, timeProvider, credential);
                if (!await api.IsLoggedInAsync(cancellationToken)) // Cached, so the loop condition sends no second nav.
                    logger.LogWarning("新的登录凭据未生效，请重新扫码登录。");
            }
            yield return new Connecting();
            await foreach (var update in ReceiveRoomUpdatesAsync(realRoomId.Value, api, cancellationToken))
                yield return update;
        }
    }

    // .NET exposes ERROR_SHARING_VIOLATION on Windows, but raw EWOULDBLOCK on Unix (35 on macOS, 11 on Linux).
    private static readonly int RoomLockHeldHResult = OperatingSystem.IsWindows()
        ? unchecked((int)0x80070020) : OperatingSystem.IsMacOS() ? 35 : 11;

    // ponytail: On Unix this is a best-effort flock: .NET silently skips it when DOTNET_SYSTEM_IO_DISABLEFILELOCKING is set
    // or the filesystem rejects flock (some network mounts). Fine while data/ sits on a local disk, as the README asks.
    private FileStream AcquireRoomLock(long realRoomId)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FormattableString.Invariant($"room-{realRoomId}.lock"));
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when (error.HResult == RoomLockHeldHResult)
        {
            throw new InvalidOperationException($"直播间 {realRoomId} 已有直播间连接（可能在另一个 CLI 或 GUI 里）。", error);
        }
    }

    /// <param name="refreshCancellation">Abandons a refresh B站 may already have issued, dropping the new credential unsaved;
    /// so only the caller cancels it, never the end of a room connection.</param>
    private async Task<Credential?> RefreshCredentialIfDueAsync(
        CancellationToken cancellationToken, CancellationToken refreshCancellation)
    {
        var credentials = credentialStore.Value;
        var credential = credentials.Load();
        if (credential is null || credential.CheckedAt is { } checkedAt
            && timeProvider.GetUtcNow() - checkedAt < CredentialCheckInterval) return credential;
        try
        {
            var api = new BilibiliApi(http, timeProvider, credential);
            if (await api.GetRefreshTimestampAsync(cancellationToken) is { } timestamp)
            {
                var refreshed = await api.RefreshCredentialAsync(timestamp, refreshCancellation);
                if (credentials.Save(refreshed, credential.RefreshToken))
                {
                    try
                    {
                        await new BilibiliApi(http, timeProvider, refreshed)
                            .ConfirmCredentialRefreshAsync(credential.RefreshToken, cancellationToken);
                    }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning("确认登录凭据刷新失败，保留新登录凭据：{Error}", error.Message);
                    }
                }
            }
            else
                credentials.MarkChecked(credential.RefreshToken, timeProvider.GetUtcNow());
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsTransient(error))
        {
            logger.LogWarning("检查或刷新登录凭据时网络失败，下次再试：{Error}", error.Message);
        }
        catch (CredentialRejectedException error)
        {
            if (credentials.Delete(credential.RefreshToken))
                logger.LogWarning("{Error} 请重新扫码登录。", error.Message);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // Refresh is upkeep, and nav still vets the credential on every connect, so an unexpected answer
            // (an error code from cookie/info, a changed correspond page) neither deletes it nor ends the room connection.
            logger.LogWarning("检查或刷新登录凭据失败，24 小时后再试：{Error}", error.Message);
            credentials.MarkChecked(credential.RefreshToken, timeProvider.GetUtcNow());
        }
        return credentials.Load();
    }

    private async IAsyncEnumerable<WatchUpdate> ReceiveRoomUpdatesAsync(
        long realRoomId, BilibiliApi api, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await api.GetConnectionAsync(realRoomId, cancellationToken);
        using var socket = await connectWebSocket(connection.Server, cancellationToken);
        var authentication = DanmakuProtocol.CreateAuthentication(connection.Mid, realRoomId, connection.Token, connection.Buvid);
        using var idleTimeout = new CancellationTokenSource(IdleTimeout, timeProvider);
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleTimeout.Token);
        await socket.SendAsync(authentication, WebSocketMessageType.Binary, true, connectionStop.Token);
        Task heartbeat = Task.CompletedTask;
        Task credentialChecks = Task.CompletedTask;
        try
        {
            var connected = false;
            while (true)
            {
                var bytes = await ReceiveMessageAsync(socket, idleTimeout, connectionStop.Token);
                var receivedAt = timeProvider.GetLocalNow();
                foreach (var decoded in DanmakuProtocol.Decode(bytes, receivedAt, connected, logger))
                {
                    connectionStop.Token.ThrowIfCancellationRequested();
                    if (decoded is DanmakuProtocol.AuthenticationReply auth)
                    {
                        if (!auth.Success)
                            throw new InvalidOperationException("弹幕服务器认证失败。");
                        if (!connected)
                        {
                            connected = true;
                            heartbeat = SendHeartbeatsAsync(socket, connectionStop);
                            credentialChecks = RefreshCredentialWhileConnectedAsync(connectionStop, cancellationToken);
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
            try { await Task.WhenAll(heartbeat, credentialChecks); }
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

    private async Task RefreshCredentialWhileConnectedAsync(CancellationTokenSource stop, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var checkedAt = credentialStore.Value.Load()?.CheckedAt;
                var delay = checkedAt + CredentialCheckInterval - timeProvider.GetUtcNow();
                // An overdue check failed transiently; give it another opportunity without a busy loop.
                await Task.Delay(delay is { } remaining && remaining > TimeSpan.Zero
                    ? remaining : TimeSpan.FromSeconds(30), timeProvider, stop.Token);
                // Signed out elsewhere: this connection keeps going and the next reconnect asks for a scan.
                if (credentialStore.Value.Load() is null) return;
                // Gone after the check means B站 rejected the refresh and the credential was deleted.
                if (await RefreshCredentialIfDueAsync(stop.Token, cancellationToken) is null)
                {
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        // The connection is ending anyway; checked_at is unchanged, so the reconnect checks again and meets any real error there.
        catch when (stop.IsCancellationRequested) { }
        catch
        {
            await stop.CancelAsync();
            throw;
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
