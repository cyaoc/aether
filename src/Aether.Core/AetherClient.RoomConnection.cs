using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed partial class AetherClient
{
    /// <summary>One room connection: from resolving the room until it ends, across every reconnect.
    /// Runs on its own and reports to <paramref name="output"/>; whoever reads it never paces it.</summary>
    private sealed class RoomConnection(AetherClient client, long roomId, Settings settings, ChannelWriter<WatchUpdate> output, bool enableStreamOverlay)
    {
        // No data at all for this long, not even a heartbeat reply, means the room connection is dead.
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

        // The web player's own fallback (danmu_length||20) when getInfoByUser gives no 弹幕字数上限.
        private const int FallbackDanmakuLengthLimit = 20;

        /// <summary>The settings this room connection started with; reconnects keep them.</summary>
        public Settings Settings { get; } = settings;

        // Each is set once and kept across reconnects.
        private long? realRoomId;
        private long broadcasterUid;
        private IDisposable? roomScope;
        private DanmakuProtocol.Decoder? decoder;

        // Why the current attempt was stopped, where its exception would only say it was canceled; cleared before each attempt.
        private string? stopReason;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var animationStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Made once the room connection starts, and kept across reconnects.
            Region<RoomConnectionState>? region = null;
            var reconnecting = false;
            var retrySeconds = 1;
            try
            {
                while (true)
                {
                    var connected = false;
                    stopReason = null;
                    try
                    {
                        if (realRoomId is null)
                            (realRoomId, broadcasterUid) = await client.Api(credential: null)
                                .ResolveRoomAsync(roomId, cancellationToken);
                        region ??= Start(realRoomId.Value, animationStop.Token);
                        await foreach (var update in AttemptAsync(realRoomId.Value, region, cancellationToken))
                        {
                            if (reconnecting && update is Connecting) continue;
                            if (update is Connected)
                            {
                                connected = true;
                                retrySeconds = 1;
                            }
                            // Never wait here on the reader or on anything slow: the idle deadline only moves as packets arrive.
                            await output.WriteAsync(update, cancellationToken);
                        }
                        return;
                    }
                    catch (Exception error) when (IsRetryable(error, cancellationToken))
                    {
                        // A stop this attempt made itself surfaces as a cancellation the reason fully explains;
                        // any other failure is its own cause, even if a stop was requested at the same moment.
                        var reason = error is OperationCanceledException ? stopReason : null;
                        var exception = reason is null ? error : null;
                        if (connected)
                            client.logger.LogWarning(exception, "直播间连接中断，{Seconds} 秒后重试：{Error}", retrySeconds, reason ?? error.Message);
                        else
                            client.logger.LogWarning(exception, "连接弹幕服务器失败，{Seconds} 秒后重试：{Error}", retrySeconds, reason ?? error.Message);
                    }
                    // The caller records every failure for the process; this copy only tells the room's file how it ended.
                    catch (Exception error) when (region is not null && !cancellationToken.IsCancellationRequested)
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
                animationStop.Cancel();
                if (region is not null) await EndAsync(region);
                roomScope?.Dispose();
            }
        }

        /// <summary>Begins the room's log scope, then the region inside it, so that its steps log to the room's file too.</summary>
        private Region<RoomConnectionState> Start(long realRoomId, CancellationToken animationStop)
        {
            roomScope = client.logger.BeginRoomScope(realRoomId);
            client.logger.LogInformation("直播间连接开始");
            decoder = new DanmakuProtocol.Decoder(realRoomId, client.logger,
                () => Settings.WelcomeEnabled.Value || client.WebService.Video.Settings?.EntryAnimationEnabled.Value == true);
            return new(post => CreateState(realRoomId, post, animationStop), client.steps, client.logger);
        }

        /// <summary>The region's state: the 发送队列, and the features in the order they handle each event.</summary>
        private RoomConnectionState CreateState(long realRoomId, Action<Action> post, CancellationToken animationStop)
        {
            var sendQueue = new SendQueue(client.timeProvider, client.logger,
                TimeSpan.FromSeconds(Settings.SendIntervalSeconds.Value), output, post);
            List<IRoomFeature> features = [];
            if (enableStreamOverlay)
            {
                features.Add(new LotteryFeature(client.Lottery, client.logger, realRoomId));
                features.Add(new GiftBoardFeature(client.GiftBoard, realRoomId));
            }
            if (Settings.BlindBoxEnabled.Value) features.Add(new BlindBoxQueryFeature(Settings, sendQueue, client.logger));
            features.Add(new GiftRecordFeature(new(() => client.stores.Value.Gifts, client.logger,
                realRoomId, Settings.BlindBoxEnabled.Value), sendQueue, Settings.BlindBoxAnnounce.Value));
            if (enableStreamOverlay)
            {
                features.Add(new GiftRoundFeature(client.timeProvider, client.logger, realRoomId,
                    round => { client.GiftAnimation.Enqueue(round, animationStop); output.TryWrite(round); }, post));
                features.Add(new EntryAnimationFeature(client.EntryAnimation, animationStop));
            }
            if (Settings.WelcomeEnabled.Value) features.Add(new WelcomeFeature(Settings, sendQueue));
            return new(sendQueue, features);
        }

        /// <summary>Clears the 发送队列 and tells the features the room connection has ended, then stops the region: a timer
        /// due after that posts to nothing.</summary>
        private async Task EndAsync(Region<RoomConnectionState> region)
        {
            var discarded = await region.Run(s => s.SendQueue.Clear());
            client.logger.LogInformation("直播间连接结束，发送队列丢掉 {Count} 条待发弹幕", discarded);
            await region.Run(s => s.Handle(new RoomConnectionEnded()));
            region.Stop();
        }

        private async IAsyncEnumerable<WatchUpdate> AttemptAsync(long realRoomId, Region<RoomConnectionState> region,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var credential = await client.RefreshCredentialIfDueAsync(cancellationToken, cancellationToken);
            var api = client.Api(credential);
            while (!await api.IsLoggedInAsync(cancellationToken))
            {
                await foreach (var update in client.LoginCoreAsync(cancellationToken))
                    if (update is LoginQrCode qr) yield return new WatchQrCode(qr.Content);
                credential = await client.account.LoadAsync();
                api = client.Api(credential);
                if (!await api.IsLoggedInAsync(cancellationToken)) // Cached, so the loop condition sends no second nav.
                    client.logger.LogWarning("新的登录凭据未生效，请重新扫码登录。");
            }
            yield return new Connecting();
            await foreach (var update in ReceiveUpdatesAsync(realRoomId, api, credential?.CheckedAt, region, cancellationToken))
                yield return update;
        }

        /// <summary>Decodes outside the region; each event is one step of it, which the loop waits for.</summary>
        private async IAsyncEnumerable<WatchUpdate> ReceiveUpdatesAsync(long realRoomId, BilibiliApi api, DateTimeOffset? checkedAt,
            Region<RoomConnectionState> region, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var connection = await api.GetConnectionAsync(realRoomId, cancellationToken);
            var danmakuLengthLimit = FallbackDanmakuLengthLimit;
            if (Settings.WelcomeEnabled.Value)
            {
                try
                {
                    danmakuLengthLimit = await api.GetDanmakuLengthLimitAsync(realRoomId, cancellationToken);
                    client.logger.LogInformation("弹幕字数上限：{Limit}", danmakuLengthLimit);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    client.logger.LogWarning(error, "读取弹幕字数上限失败，按 {Limit} 处理：{Error}", danmakuLengthLimit, error.Message);
                }
            }
            using var socket = await client.connectWebSocket(connection.Server, cancellationToken);
            var authentication = DanmakuProtocol.CreateAuthentication(connection.Mid, realRoomId, connection.Token, connection.Buvid);
            using var idleTimeout = new CancellationTokenSource(IdleTimeout, client.timeProvider);
            using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleTimeout.Token);
            Task heartbeat = Task.CompletedTask;
            Task credentialChecks = Task.CompletedTask;
            Task sending = Task.CompletedTask;
            try
            {
                await socket.SendAsync(authentication, WebSocketMessageType.Binary, true, connectionStop.Token);
                var connected = false;
                while (true)
                {
                    var bytes = await ReceiveMessageAsync(socket, idleTimeout, connectionStop.Token);
                    var receivedAt = client.timeProvider.GetLocalNow();
                    var receivedTimestamp = client.timeProvider.GetTimestamp();
                    foreach (var decoded in decoder!.Decode(bytes, receivedAt, connected, receivedTimestamp))
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
                                credentialChecks = RefreshCredentialWhileConnectedAsync(checkedAt, connectionStop, cancellationToken);
                                client.logger.LogInformation("已连接弹幕服务器 {Server}", connection.Server);
                                await region.Run(s => s.Handle(new TransportConnected(danmakuLengthLimit)));
                                yield return new Connected(realRoomId, broadcasterUid);
                                // After Connected, so a send queue left over from before a reconnect reports nothing ahead of it;
                                // on the thread pool, as it would otherwise run its first SQLite query right here on the receive loop.
                                sending = Task.Run(() => SendRepliesAsync(realRoomId, region, connectionStop), CancellationToken.None);
                            }
                        }
                        else if (decoded is RoomEvent roomEvent)
                        {
                            if (roomEvent is DanmakuProtocol.DanmakuReceived danmaku)
                            {
                                // Reported first, so a shell sees the keyword before the 盲盒查询 it queues.
                                yield return danmaku.Danmaku;
                            }
                            // On the board first, and waited for: the database neither delays nor decides what the 礼物榜页 shows.
                            if (roomEvent is DanmakuProtocol.GiftsReceived gifts)
                                roomEvent = gifts with { PeriodOf = await client.GiftBoard.TakeAsync(realRoomId, gifts.Gifts) };
                            await region.Run(s => s.Handle(roomEvent));
                        }
                    }
                }
            }
            finally
            {
                // Read before teardown, which can outlast the idle deadline; a stop already explained keeps its reason.
                if (idleTimeout.IsCancellationRequested)
                    stopReason ??= FormattableString.Invariant($"{IdleTimeout.TotalSeconds} 秒未收到任何数据，包括心跳回复。");
                await region.Run(s => s.Handle(new TransportDisconnected()));
                await connectionStop.CancelAsync();
                try { await Task.WhenAll(heartbeat, credentialChecks, sending); }
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

        /// <summary>Sends queries' replies, announcements and welcomes one at a time until stopped: waits and requests happen out
        /// here, and each turn is a step, so the text sent is current.</summary>
        private Task SendRepliesAsync(long roomId, Region<RoomConnectionState> region, CancellationTokenSource stop) =>
            RunWhileConnectedAsync(stop, async () =>
            {
                var queued = await region.Run(s => s.SendQueue.Queued);
                while (await queued.WaitToReadAsync(stop.Token))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    var api = client.Api(await client.account.LoadAsync());
                    if (await region.Run(s => s.SendQueue.TakeTurn(query =>
                            client.stores.Value.BlindBoxes.Tally(roomId, query.Uid, query.Trigger.ReceivedAt, query.Period)))
                        is not { } turn) continue;
                    // Waiting can change a query's tally or an announcement's round; the next turn takes the new text.
                    if (turn.Wait > TimeSpan.Zero)
                    {
                        await Task.Delay(turn.Wait, client.timeProvider, stop.Token);
                        continue;
                    }
                    Exception? failure = null;
                    try
                    {
                        await api.SendDanmakuAsync(roomId, turn.Item is SendQueue.Welcome { Result.Mention: false } ? null : turn.Item.Uid,
                            (turn.Item as SendQueue.Query)?.Trigger.Id, turn.Text, stop.Token);
                    }
                    catch (Exception error) when (!stop.IsCancellationRequested) { failure = error; }
                    finally { await region.Run(s => s.SendQueue.CountSend(turn.Text)); }
                    await region.Run(s => s.SendQueue.EndTurn(turn, failure));
                }
            });

        // A refresh cut short by the connection ending leaves checked_at unchanged, so the reconnect checks again. The first
        // wait starts from the credential this attempt connected with, so it is armed before Connected is reported.
        private Task RefreshCredentialWhileConnectedAsync(DateTimeOffset? checkedAt, CancellationTokenSource stop,
            CancellationToken cancellationToken) => RunWhileConnectedAsync(stop, async () =>
            {
                while (true)
                {
                    var delay = checkedAt + CredentialCheckInterval - client.timeProvider.GetUtcNow();
                    // An overdue check failed transiently; give it another opportunity without a busy loop.
                    await Task.Delay(delay is { } remaining && remaining > TimeSpan.Zero
                        ? remaining : TimeSpan.FromSeconds(30), client.timeProvider, stop.Token);
                    // Gone after the check means B站 rejected the refresh and the credential was deleted.
                    if (await client.RefreshCredentialIfDueAsync(stop.Token, cancellationToken) is not { } credential)
                    {
                        if (!stop.IsCancellationRequested)
                            stopReason = "登录凭据已被删除，停止当前连接并重新登录。";
                        await stop.CancelAsync();
                        return;
                    }
                    checkedAt = credential.CheckedAt;
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
