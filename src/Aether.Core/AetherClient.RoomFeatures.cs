using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed partial class AetherClient
{
    /// <summary>The lottery rides along: its database failing is logged, and danmaku keep coming.</summary>
    private sealed class LotteryFeature(LotteryController lottery, ILogger logger, long roomId) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            switch (e)
            {
                case TransportConnected:
                    RunLottery();
                    break;
                case DanmakuProtocol.DanmakuReceived received:
                    RunLottery(received.Danmaku);
                    break;
                case TransportDisconnected:
                    lottery.Disconnect(roomId);
                    break;
                case RoomConnectionEnded:
                    lottery.EndRoom(roomId);
                    break;
            }
        }

        private void RunLottery(Danmaku? danmaku = null)
        {
            try
            {
                // Retry a failed initial load when the next danmaku arrives, without reconnecting the room.
                // shortcut: loads, entries and stop-word draws hit SQLite on the receive loop; move off it if a busy room lags.
                var state = lottery.State;
                if (!state.Connected || state.RoomId != roomId) lottery.Connect(roomId);
                if (danmaku is not null) lottery.Receive(roomId, danmaku);
            }
            catch (Exception error) when (error is SqliteException or JsonException or IOException)
            { logger.LogError(error, "抽奖读写数据库失败，弹幕照常接收：{Error}", error.Message); }
        }
    }

    private sealed class BlindBoxQueryFeature(Settings settings, SendQueue sendQueue, ILogger logger) : IRoomFeature
    {
        private readonly string dailyKeyword = settings.BlindBoxDailyKeyword.Value.Trim();
        private readonly string monthlyKeyword = settings.BlindBoxMonthlyKeyword.Value.Trim();

        public void Handle(RoomEvent e)
        {
            if (e is not DanmakuProtocol.DanmakuReceived { Danmaku: var danmaku }) return;
            var text = danmaku.Content.Trim();
            BlindBoxPeriod? period = text == dailyKeyword ? BlindBoxPeriod.Daily
                : text == monthlyKeyword ? BlindBoxPeriod.Monthly : null;
            if (period is { } matched)
            {
                if (danmaku.Uid == 0)
                    logger.LogDebug("uid 为 0 的观众发出盲盒关键字，未回复");
                else sendQueue.Enqueue(danmaku, matched);
            }
        }
    }

    private sealed class BlindBoxRecordFeature(Lazy<Stores> stores, long roomId, SendQueue sendQueue, bool announce) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            // ponytail: a synchronous SQLite write on the receive loop (busy waits up to Microsoft.Data.Sqlite's 30 s
            // command timeout, well inside the 60 s idle deadline); hand it to a background writer if a room ever stalls on it.
            if (e is DanmakuProtocol.BlindBoxesReceived received)
                sendQueue.Record(received.BlindBoxes, received.ReceivedTimestamp,
                    box => stores.Value.BlindBoxes.Save(roomId, box) && announce);
        }
    }

    private sealed class WelcomeAnimationFeature(WelcomeAnimation animation, CancellationToken roomStopped) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            if (e is DanmakuProtocol.EntryReceived { Entry: var entry, ReceivedTimestamp: var timestamp })
                animation.Enqueue(entry.Uid, entry.Nickname, timestamp, roomStopped);
        }
    }

    private sealed class WelcomeFeature(Settings settings, SendQueue sendQueue) : IRoomFeature
    {
        private readonly Welcome welcome = new(settings);
        private int danmakuLengthLimit;

        public void Handle(RoomEvent e)
        {
            switch (e)
            {
                case TransportConnected connected:
                    danmakuLengthLimit = connected.DanmakuLengthLimit;
                    break;
                case DanmakuProtocol.EntryReceived { Entry: var entry, ReceivedTimestamp: var timestamp }:
                    if (welcome.Evaluate(entry, danmakuLengthLimit) is { } result)
                        sendQueue.EnqueueWelcome(entry.Uid, entry.Nickname, timestamp, result);
                    break;
            }
        }
    }
}
