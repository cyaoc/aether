namespace Aether.Core;

/// <summary>What the 桌宠页 is told, in the web service region (ADR 0017): a state first and whenever it changes, then each thing
/// that happens as one event. The page decides what to play; while the 桌宠 is off it is told only that. Her lines are
/// <paramref name="character"/>'s, read before any step.</summary>
internal sealed class DesktopPet(WebService service, Character character)
{
    /// <summary>What happens, by the name the page is told it under.</summary>
    public const string Gift = "gift", BlindBox = "blindBox", Guard = "guard", Entry = "entry", Danmaku = "danmaku",
        Test = "testShow", StopTest = "stopTestShow";

    private bool roomConnected;
    private Lottery? lottery;
    private GiftBoardState? giftBoard;
    // The 试演 that plays, by the number its page reports it over with, and the pages told it: once none of them is left, none
    // plays it. A page reports a 试演 it played after another started, so only the one that plays is over.
    private int trials;
    private (int Id, WebService.Connection[] Pages)? trial;

    public Subscribers<SseEvent> Subscribers { get; } = new();

    private bool Enabled => service.Settings?.DesktopPetEnabled.Value == true;

    public SseEvent State => new("state", service.Settings is { DesktopPetEnabled.Value: true } settings
        ? new DesktopPetState(true, roomConnected, lottery, giftBoard, DesktopPetSettings.Of(settings), settings.DesktopPetLines(character))
        : new DesktopPetState(false));

    /// <summary>New settings may turn the 桌宠 on or off, and change how she plays and what she says; off, she plays no 试演.</summary>
    public void Applied()
    {
        if (!Enabled) trial = null;
        Subscribers.Publish(State);
    }

    /// <summary>A reconnect is still the same room connection, so only its start and its end change the state.</summary>
    public void SetRoom(bool connected)
    {
        if (roomConnected == connected) return;
        roomConnected = connected;
        Publish(State);
    }

    /// <summary>The 抽奖页's example is no lottery, and what the streamer keeps off air stays off the 桌宠 too.</summary>
    public void SetLottery(LotteryUpdate update)
    {
        Lottery? onAir = update.Live.Lottery switch
        {
            EntryLottery { Hidden: true } => null,
            EntryLottery entry => entry with { Draws = [.. entry.Draws.Where(draw => !draw.HiddenFromStream)] },
            PoolLottery pool => pool with { Draws = [.. pool.Draws.Where(draw => !draw.HiddenFromStream)] },
            _ => null
        };
        if (update.Example is not null || onAir is null && lottery is null) return;
        lottery = onAir;
        Publish(State);
    }

    /// <summary>Only a running 期 is told: an ended one has no new first place.</summary>
    public void SetGiftBoard(GiftBoardState state)
    {
        if (!state.Running && giftBoard is null) return;
        giftBoard = state.Running ? state : null;
        Publish(State);
    }

    public void Tell(string name, object data) => Publish(new(name, data));

    /// <summary>Has the page play <paramref name="show"/> now; while no page would, the GUI is told it ended, with why.</summary>
    public void TestShow(string show, string? variant, string nickname)
    {
        var refused = !Enabled ? "桌宠没有打开。" : WebService.WhyNotRunning(service.Status)
            ?? (service.Status.DesktopPetConnections == 0 ? "桌宠页没有推流软件连着。" : null);
        if (refused is not null)
        {
            service.Publish(new DesktopPetTestEnded(refused));
            return;
        }
        trial = (++trials, Pages);
        Tell(Test, new { show, variant, nickname, id = trials });
    }

    public void StopTestShow()
    {
        trial = null;
        Tell(StopTest, new());
    }

    /// <summary>The page played the 试演 <paramref name="id"/> out, or cut it short: if it is the one that plays, the GUI can
    /// offer another.</summary>
    public void TestShowEnded(int id)
    {
        if (trial?.Id == id) Ended();
    }

    /// <summary>A 试演 is over too once every page told it is gone, such as one the streaming software reloaded.</summary>
    public void ConnectionsChanged()
    {
        if (trial is { } playing && !playing.Pages.Intersect(Pages).Any()) Ended();
    }

    private void Ended()
    {
        trial = null;
        service.Publish(new DesktopPetTestEnded());
    }

    private WebService.Connection[] Pages => [.. service.Connected(null).Where(connection => connection.Page is DesktopPetPage)];

    private void Publish(SseEvent message)
    {
        if (Enabled) Subscribers.Publish(message);
    }
}

/// <summary>The 桌宠页's state: whether the 桌宠 is on and, only then, whether a room is connected, which her quiet-room clocks
/// start from, the lottery as it is on air, whose DrawUntil is when a draw is revealed, the 礼物榜 while its 期 runs, her settings
/// and the current character's lines by kind, with the kinds the streamer changed.</summary>
internal sealed record DesktopPetState(bool Enabled, bool RoomConnected = false, Lottery? Lottery = null, GiftBoardState? GiftBoard = null,
    DesktopPetSettings? Settings = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? Lines = null);

/// <summary>How the streamer set the 桌宠 to play: the 演出表 rows turned off; the 中礼物 and 大礼物 thresholds, in 金瓜子 as the
/// rounds' amounts are; the quiet minutes before 无聊, 沮丧 and 犯困, 0 for never; the least and most seconds between 日常小动作; and
/// the percent chance one goes wrong.</summary>
internal sealed record DesktopPetSettings(IReadOnlyList<string> Off, long MidGift, long BigGift, int BoredMinutes, int GloomyMinutes,
    int SleepyMinutes, int DailyMinSeconds, int DailyMaxSeconds, int MishapPercent)
{
    public static DesktopPetSettings Of(Settings settings) => new(settings.DesktopPetShowsOff.Value, Battery.ToCoins(settings.DesktopPetMidGift.Value),
        Battery.ToCoins(settings.DesktopPetBigGift.Value), settings.DesktopPetBoredMinutes.Value, settings.DesktopPetGloomyMinutes.Value,
        settings.DesktopPetSleepyMinutes.Value, settings.DesktopPetDailyMinSeconds.Value, settings.DesktopPetDailyMaxSeconds.Value,
        settings.DesktopPetMishapPercent.Value);
}

/// <summary>A viewer's 一轮送礼 as it stands after a gift message, and once more as it ends: its gifts' 投入, how many, the one
/// of them priced highest and how many of it; its 盲盒' 投入 and boxes; and whether a 浪漫城堡 came, sent or opened. Its 大航海
/// play on their own and count in none of it. Amounts are 金瓜子.</summary>
internal sealed record GiftRoundProgress(long Uid, string Nickname, long Spend, long Count, string TopGift, long TopCount,
    long BoxSpend, long Boxes, bool Castle, bool Ended)
{
    public static GiftRoundProgress Of(long uid, string nickname, IReadOnlyCollection<Gift> sent, bool ended)
    {
        var gifts = sent.Where(gift => gift.Blind is null).ToArray();
        var boxes = sent.Where(gift => gift.Blind is not null).ToArray();
        var top = gifts.OrderByDescending(gift => gift.Price).FirstOrDefault();
        return new(uid, nickname, gifts.Sum(gift => gift.Spend), gifts.Sum(gift => gift.Num), top?.GiftName ?? "",
            gifts.Where(gift => gift.GiftId == top?.GiftId).Sum(gift => gift.Num), boxes.Sum(box => box.Spend), boxes.Sum(box => box.Num),
            sent.Any(gift => gift.IsCastle), ended);
    }
}

/// <summary>A viewer's 一轮盲盒 as its 盲盒播报 reported it, sent or given up, or without one as the 一轮送礼 ended: the tally, how
/// many boxes, the gift opened priced highest (one of it) and whether a 浪漫城堡 came out. One given up is still told, as one
/// without a 盲盒播报 is: she stops shaking the boxes only for their result.</summary>
internal sealed record BlindBoxResult(long Uid, string Nickname, BlindBoxTally Tally, long Boxes, string BestGift, long BestPrice, bool Castle)
{
    public static BlindBoxResult Of(long uid, string nickname, IReadOnlyCollection<Gift> boxes)
    {
        var best = boxes.OrderByDescending(box => box.Price).First();
        return new(uid, nickname, BlindBoxTally.Of(boxes), boxes.Sum(box => box.Num), best.GiftName, best.Price, boxes.Any(box => box.IsCastle));
    }
}
