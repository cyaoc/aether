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

    /// <summary>A room connection region's state (ADR 0017): the features and the 发送队列 they share, touched only in its
    /// steps. An event is one step, which every feature handles in turn.</summary>
    private sealed class RoomConnectionState(SendQueue sendQueue, IReadOnlyList<IRoomFeature> features)
    {
        public SendQueue SendQueue => sendQueue;

        public void Handle(RoomEvent e)
        {
            foreach (var feature in features) feature.Handle(e);
        }
    }

    private sealed class GiftRoundFeature(TimeProvider time, ILogger logger, long roomId,
        Action<GiftRoundEnded> ended, Action<Action> post) : IRoomFeature
    {
        private sealed class Round(long uid, string nickname)
        {
            public long Uid = uid;
            public string Nickname = nickname;
            public long Spend;
            public QuietTimer Timer = null!;
        }
        private readonly Dictionary<long, Round> rounds = [];

        public void Handle(RoomEvent e)
        {
            if (e is RoomConnectionEnded)
            {
                foreach (var round in rounds.Values) round.Timer.Dispose();
                rounds.Clear();
            }
            if (e is not DanmakuProtocol.GiftsReceived received) return;
            // shortcut: no tid deduplication; add it if real captures show duplicate gift pushes.
            foreach (var gift in received.Gifts)
            {
                if (gift.Uid == 0)
                {
                    logger.LogInformation("uid 为 0 的观众送礼，未计入一轮送礼");
                    continue;
                }
                if (!rounds.TryGetValue(gift.Uid, out var round))
                {
                    round = new(gift.Uid, gift.Nickname);
                    rounds.Add(gift.Uid, round);
                    round.Timer = new QuietTimer(time, post, () =>
                    {
                        rounds.Remove(round.Uid);
                        round.Timer.Dispose();
                        ended(new(roomId, round.Uid, round.Nickname, round.Spend,
                            time.GetUtcNow(), time.GetTimestamp()));
                    });
                }
                round.Nickname = gift.Nickname;
                round.Spend = checked(round.Spend + gift.Spend);
                round.Timer.Reset(received.ReceivedTimestamp);
            }
        }
    }

    /// <summary>The 礼物榜 rides along like the lottery: it reads the database in its own region, so danmaku keep coming. A
    /// reconnect is still the same room connection and leaves the board as it is.</summary>
    private sealed class GiftBoardFeature(GiftBoardFacade board, long roomId) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            switch (e)
            {
                case TransportConnected:
                    board.Connect(roomId);
                    break;
                case RoomConnectionEnded:
                    board.EndRoom(roomId);
                    break;
            }
        }
    }

    private sealed class BlindBoxQueryFeature(Settings settings, SendQueue sendQueue, ILogger logger) : IRoomFeature
    {
        private readonly string dailyKeyword = settings.BlindBoxDailyKeyword.Value.Trim();
        private readonly string monthlyKeyword = settings.BlindBoxMonthlyKeyword.Value.Trim();
        private readonly string yearlyKeyword = settings.BlindBoxYearlyKeyword.Value.Trim();

        public void Handle(RoomEvent e)
        {
            if (e is not DanmakuProtocol.DanmakuReceived { Danmaku: var danmaku }) return;
            var text = danmaku.Content.Trim();
            BlindBoxPeriod? period = text == dailyKeyword ? BlindBoxPeriod.Daily
                : text == monthlyKeyword ? BlindBoxPeriod.Monthly
                : text == yearlyKeyword ? BlindBoxPeriod.Yearly : null;
            if (period is { } matched)
            {
                if (danmaku.Uid == 0)
                    logger.LogDebug("uid 为 0 的观众发出盲盒关键字，未回复");
                else sendQueue.Enqueue(danmaku, matched);
            }
        }
    }

    /// <summary>Writes the room's paid gifts to the 收礼记录; a 盲盒 newly recorded for the 盲盒统计 joins its viewer's 一轮盲盒
    /// when announcing is on.</summary>
    private sealed class GiftRecordFeature(GiftRecording recording, SendQueue sendQueue, bool announce) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            // ponytail: a synchronous SQLite write in a step the receive loop waits for (busy waits up to Microsoft.Data.Sqlite's
            // 30 s command timeout, well inside the 60 s idle deadline); hand it to a background writer if a room ever stalls on it.
            if (e is DanmakuProtocol.GiftsReceived { Gifts: var gifts, ReceivedTimestamp: var receivedAt, PeriodOf: var periodOf })
                recording.Record(periodOf, write => sendQueue.Record(gifts, receivedAt, gift => write(gift) && announce));
        }
    }

    private sealed class EntryAnimationFeature(WebServiceFacade service, CancellationToken roomStopped) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            if (e is DanmakuProtocol.EntryReceived { Entry: var entry, ReceivedTimestamp: var timestamp })
                service.PlayEntry(entry, timestamp, roomStopped);
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
