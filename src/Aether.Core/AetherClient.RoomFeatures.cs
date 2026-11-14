using Microsoft.Extensions.Logging;

namespace Aether.Core;

public sealed partial class AetherClient
{
    /// <summary>The lottery rides along in its own region, which takes each event without the room waiting for it.</summary>
    private sealed class LotteryFeature(LotteryFacade lottery, long roomId) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            switch (e)
            {
                case TransportConnected:
                    lottery.Connect(roomId);
                    break;
                case DanmakuProtocol.DanmakuReceived received:
                    lottery.Receive(roomId, received.Danmaku);
                    break;
                case TransportDisconnected:
                    lottery.Disconnect(roomId);
                    break;
                case RoomConnectionEnded:
                    lottery.EndRoom(roomId);
                    break;
            }
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

    /// <summary>Where a 一轮送礼 reports: the 送礼动画 takes its end; the 桌宠 how it stands after each message and as it ends; and
    /// <paramref name="BlindBoxes"/> the result of its 盲盒 as it ends, none while a 盲盒播报 reports them.</summary>
    private sealed record GiftRoundReports(Action<GiftRoundEnded> Ended, Action<string, object> TellDesktopPet, Action<BlindBoxResult>? BlindBoxes);

    private sealed class GiftRoundFeature(StepTimers timers, ILogger logger, long roomId, GiftRoundReports reports) : IRoomFeature
    {
        private readonly TimeProvider time = timers.Time;
        private sealed class Round(long uid)
        {
            public long Uid = uid;
            public string Nickname = "";
            public long Spend;
            // The 桌宠's part, in the order received: its 大航海 play on their own.
            public readonly List<Gift> Gifts = [];
            public QuietTimer Timer = null!;
            public GiftRoundProgress Progress(bool ended) => GiftRoundProgress.Of(Uid, Nickname, Gifts, ended);
        }
        private readonly Dictionary<long, Round> rounds = [];
        // A push B站 repeats counts once, as the 收礼记录 writes it once, so a round's boxes are the ones its 盲盒播报 reports.
        // shortcut: the tids are kept for the whole room connection; forget old ones if a connection lasting days holds too many.
        private readonly HashSet<string> tids = [];

        public void Handle(RoomEvent e)
        {
            if (e is RoomConnectionEnded)
            {
                foreach (var round in rounds.Values) round.Timer.Dispose();
                rounds.Clear();
                tids.Clear();
            }
            if (e is not DanmakuProtocol.GiftsReceived received) return;
            foreach (var viewer in received.Gifts.Where(gift => gift.Tid is not { } tid || tids.Add(tid)).GroupBy(gift => gift.Uid))
            {
                if (viewer.Key == 0)
                {
                    logger.LogInformation("uid 为 0 的观众送礼，未计入一轮送礼");
                    continue;
                }
                var round = RoundOf(viewer.Key);
                round.Nickname = viewer.Last().Nickname;
                round.Spend = checked(round.Spend + viewer.Sum(gift => gift.Spend));
                var gifts = viewer.Where(gift => gift.GuardLevel == 0).ToArray();
                round.Gifts.AddRange(gifts);
                if (gifts.Length > 0)
                    reports.TellDesktopPet(DesktopPet.Gift, round.Progress(false) with { Items = [.. gifts.Select(GiftItem.Of)] });
                round.Timer.Reset(received.ReceivedTimestamp);
            }
        }

        private Round RoundOf(long uid)
        {
            if (rounds.TryGetValue(uid, out var round)) return round;
            round = new(uid);
            rounds.Add(uid, round);
            round.Timer = new QuietTimer(timers, () => End(round));
            return round;
        }

        private void End(Round round)
        {
            rounds.Remove(round.Uid);
            round.Timer.Dispose();
            reports.Ended(new(roomId, round.Uid, round.Nickname, round.Spend, time.GetUtcNow(), time.GetTimestamp()));
            if (round.Gifts.Count == 0) return;
            reports.TellDesktopPet(DesktopPet.Gift, round.Progress(true));
            var boxes = round.Gifts.Where(gift => gift.Blind is not null).ToArray();
            if (boxes.Length > 0) reports.BlindBoxes?.Invoke(BlindBoxResult.Of(round.Uid, round.Nickname, boxes));
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

    /// <summary>Tells the 桌宠 what happens in the room; whether it plays is up to the web service region.</summary>
    private sealed class DesktopPetFeature(WebServiceFacade service) : IRoomFeature
    {
        public void Handle(RoomEvent e)
        {
            switch (e)
            {
                case TransportConnected:
                    service.SetDesktopPetRoom(true);
                    break;
                case RoomConnectionEnded:
                    service.SetDesktopPetRoom(false);
                    break;
                case DanmakuProtocol.EntryReceived { Entry: var entry }:
                    service.TellDesktopPet(DesktopPet.Entry, new { entry.Uid, entry.Nickname });
                    break;
                // Only that something happened: what a viewer wrote never reaches the stream.
                case DanmakuProtocol.DanmakuReceived:
                    service.TellDesktopPet(DesktopPet.Danmaku, new());
                    break;
                case DanmakuProtocol.GiftsReceived { Gifts: var gifts }:
                    foreach (var gift in gifts.Where(gift => gift.GuardLevel > 0))
                        service.TellDesktopPet(DesktopPet.Guard, new { gift.Uid, gift.Nickname, gift.GuardLevel, gift.Spend });
                    break;
            }
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
