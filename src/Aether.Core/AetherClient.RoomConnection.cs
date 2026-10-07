using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed partial class AetherClient
{
    /// <summary>One room connection: from resolving the room until it ends, across every reconnect.
    /// Runs on its own and reports to <paramref name="output"/>; whoever reads it never paces it.</summary>
    private sealed class RoomConnection(AetherClient client, long roomId, Settings settings, ChannelWriter<WatchUpdate> output)
    {
        // No data at all for this long, not even a heartbeat reply, means the room connection is dead.
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

        // .NET exposes ERROR_SHARING_VIOLATION on Windows, but raw EWOULDBLOCK on Unix (35 on macOS, 11 on Linux).
        private static readonly int RoomLockHeldHResult = OperatingSystem.IsWindows()
            ? unchecked((int)0x80070020) : OperatingSystem.IsMacOS() ? 35 : 11;

        /// <summary>The settings this room connection started with; reconnects keep them.</summary>
        public Settings Settings { get; } = settings;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            long? realRoomId = null;
            FileStream? roomLock = null;
            IDisposable? roomScope = null;
            var reconnecting = false;
            var retrySeconds = 1;
            try
            {
                while (true)
                {
                    try
                    {
                        realRoomId ??= await new BilibiliApi(client.http, client.timeProvider, credential: null)
                            .ResolveRoomIdAsync(roomId, cancellationToken);
                        roomLock ??= AcquireRoomLock(realRoomId.Value);
                        roomScope ??= client.logger.BeginScope(
                            new Dictionary<string, object> { [FileLogging.RoomIdProperty] = realRoomId.Value });
                        await foreach (var update in AttemptAsync(realRoomId.Value, cancellationToken))
                        {
                            if (reconnecting && update is Connecting) continue;
                            if (update is Connected) retrySeconds = 1;
                            await output.WriteAsync(update, cancellationToken);
                        }
                        return;
                    }
                    catch (Exception error) when (IsRetryable(error, cancellationToken))
                    {
                        client.logger.LogWarning("直播间连接中断，{Seconds} 秒后重试：{Error}", retrySeconds, error.Message);
                    }
                    // The caller records every failure for the process; this copy only tells the room's file how it ended.
                    catch (Exception error) when (roomLock is not null && !cancellationToken.IsCancellationRequested)
                    {
                        client.logger.LogError(error, "直播间连接失败");
                        throw;
                    }
                    reconnecting = true;
                    // Armed before Reconnecting is reported, so a reader that sees it knows the backoff has begun.
                    var backoff = Task.Delay(TimeSpan.FromSeconds(retrySeconds), client.timeProvider, cancellationToken);
                    await output.WriteAsync(new Reconnecting(), cancellationToken);
                    await backoff;
                    retrySeconds = Math.Min(retrySeconds * 2, 30);
                }
            }
            finally
            {
                roomScope?.Dispose();
                roomLock?.Dispose();
            }
        }

        private async IAsyncEnumerable<WatchUpdate> AttemptAsync(
            long realRoomId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var credentials = client.credentialStore.Value;
            var credential = await client.RefreshCredentialIfDueAsync(cancellationToken, cancellationToken);
            var api = new BilibiliApi(client.http, client.timeProvider, credential);
            while (!await api.IsLoggedInAsync(cancellationToken))
            {
                await foreach (var update in client.LoginCoreAsync(cancellationToken,
                    acceptExternalCredential: true, previousToken: credential?.RefreshToken))
                    if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
                credential = credentials.Load();
                api = new BilibiliApi(client.http, client.timeProvider, credential);
                if (!await api.IsLoggedInAsync(cancellationToken)) // Cached, so the loop condition sends no second nav.
                    client.logger.LogWarning("新的登录凭据未生效，请重新扫码登录。");
            }
            yield return new Connecting();
            await foreach (var update in ReceiveUpdatesAsync(realRoomId, api, cancellationToken))
                yield return update;
        }

        // ponytail: On Unix this is a best-effort flock: .NET silently skips it when DOTNET_SYSTEM_IO_DISABLEFILELOCKING is set
        // or the filesystem rejects flock (some network mounts). Fine while data/ sits on a local disk, as the README asks.
        // For OpenOrCreate + FileShare.None + DeleteOnClose, .NET verifies the inode after locking on Unix
        // and reopens if another owner deleted/replaced the path during handoff.
        private FileStream AcquireRoomLock(long realRoomId)
        {
            Directory.CreateDirectory(client.dataDirectory);
            var path = Path.Combine(client.dataDirectory, FormattableString.Invariant($"room-{realRoomId}.lock"));
            try
            {
                return new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None,
                    Options = FileOptions.DeleteOnClose,
                });
            }
            catch (IOException error) when (error.HResult == RoomLockHeldHResult)
            {
                throw new InvalidOperationException($"直播间 {realRoomId} 已有直播间连接（可能在另一个 CLI 或 GUI 里）。", error);
            }
        }

        private async IAsyncEnumerable<WatchUpdate> ReceiveUpdatesAsync(
            long realRoomId, BilibiliApi api, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var connection = await api.GetConnectionAsync(realRoomId, cancellationToken);
            using var socket = await client.connectWebSocket(connection.Server, cancellationToken);
            var authentication = DanmakuProtocol.CreateAuthentication(connection.Mid, realRoomId, connection.Token, connection.Buvid);
            using var idleTimeout = new CancellationTokenSource(IdleTimeout, client.timeProvider);
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
                    var receivedAt = client.timeProvider.GetLocalNow();
                    foreach (var decoded in DanmakuProtocol.Decode(bytes, receivedAt, connected, client.logger))
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
                        using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1), client.timeProvider);
                        try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closeTimeout.Token); }
                        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
                        { client.logger.LogDebug(error, "关闭弹幕连接时传输已不可用"); }
                    }
                }
            }
        }

        // A refresh cut short by the connection ending leaves checked_at unchanged, so the reconnect checks again.
        private Task RefreshCredentialWhileConnectedAsync(CancellationTokenSource stop, CancellationToken cancellationToken) =>
            RunWhileConnectedAsync(stop, async () =>
            {
                while (true)
                {
                    var checkedAt = client.credentialStore.Value.Load()?.CheckedAt;
                    var delay = checkedAt + CredentialCheckInterval - client.timeProvider.GetUtcNow();
                    // An overdue check failed transiently; give it another opportunity without a busy loop.
                    await Task.Delay(delay is { } remaining && remaining > TimeSpan.Zero
                        ? remaining : TimeSpan.FromSeconds(30), client.timeProvider, stop.Token);
                    // Signed out elsewhere: this connection keeps going and the next reconnect asks for a scan.
                    if (client.credentialStore.Value.Load() is null) return;
                    // Gone after the check means B站 rejected the refresh and the credential was deleted.
                    if (await client.RefreshCredentialIfDueAsync(stop.Token, cancellationToken) is null)
                    {
                        await stop.CancelAsync();
                        return;
                    }
                }
            });

        private Task SendHeartbeatsAsync(WebSocket socket, CancellationTokenSource stop) => RunWhileConnectedAsync(stop, async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), client.timeProvider);
            while (await timer.WaitForNextTickAsync(stop.Token))
                await socket.SendAsync(DanmakuProtocol.CreateHeartbeat(), WebSocketMessageType.Binary, true, stop.Token);
        });

        /// <summary>Runs one of the room connection's background loops. Anything after the connection stops is just the
        /// connection ending; any other failure stops it too, so the receive loop wakes instead of leaving the room connection hanging.</summary>
        private static async Task RunWhileConnectedAsync(CancellationTokenSource stop, Func<Task> loop)
        {
            try { await loop(); }
            catch when (stop.IsCancellationRequested) { }
            catch
            {
                await stop.CancelAsync();
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
    }
}
