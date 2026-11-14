namespace Aether.Core;

/// <summary>What the 桌宠页 is told, in the web service region (ADR 0017): a state first and whenever it changes, then each thing
/// that happens as one event. The page decides what to play; while the 桌宠 is off it is told only that. Her lines are
/// <paramref name="character"/>'s, read before any step.</summary>
internal sealed class DesktopPet(WebService service, Character character)
{
    /// <summary>What happens, by the name the page is told it under.</summary>
    public const string Gift = "gift", BlindBox = "blindBox", Guard = "guard", Entry = "entry", Danmaku = "danmaku",
        Test = "testShow", StopTest = "stopTestShow", Summon = "summon";

    private bool roomConnected;
    private bool pk;
    private Lottery? lottery;
    private GiftBoardState? giftBoard;
    // The 试演 that plays, by the number its page reports it over with, and the pages told it: once none of them is left, none
    // plays it. A page reports a 试演 it played after another started, so only the one that plays is over.
    private int trials;
    private (int Id, WebService.Connection[] Pages)? trial;
    // 叫它来 told the pages and none has said the 捣蛋鬼 set off; how many said it set off and not yet that it left; and the pages
    // there as it was told or set off last: once none of them is left, it has not come.
    // shortcut: reports do not say which page sent them, so with two streaming software open, one reloaded during a 来访 counts it
    // on until the other goes too; tell the page apart in its reports if that is ever seen.
    private bool summoned;
    private int visiting;
    private WebService.Connection[] visitPages = [];

    public Subscribers<SseEvent> Subscribers { get; } = new();

    /// <summary>The GUI's 叫它来, told each time anything it depends on may have changed.</summary>
    public Subscribers<PranksterSummon> SummonSubscribers { get; } = new();

    private bool Enabled => service.Settings?.DesktopPetEnabled.Value == true;

    public SseEvent State => new("state", service.Settings is { DesktopPetEnabled.Value: true } settings
        ? new DesktopPetState(true, roomConnected, pk, lottery, giftBoard, DesktopPetSettings.Of(settings), settings.DesktopPetLines(character))
        : new DesktopPetState(false));

    /// <summary>Whether the streamer can 叫它来, and where the 捣蛋鬼 is: on the page while any page has it there.</summary>
    public PranksterSummon SummonState => new(WhyNoSummon(),
        visiting > 0 ? PranksterVisit.OnStage : summoned ? PranksterVisit.OnTheWay : PranksterVisit.Away);

    /// <summary>New settings may turn the 桌宠 on or off, and change how she plays and what she says; off, she plays no 试演 and the
    /// 捣蛋鬼 is gone.</summary>
    public void Applied()
    {
        if (!Enabled) (trial, summoned, visiting) = (null, false, 0);
        Subscribers.Publish(State);
        PublishSummon();
    }

    /// <summary>A reconnect is still the same room connection, so only its start and its end change the state.</summary>
    public void SetRoom(bool connected)
    {
        if (roomConnected == connected) return;
        roomConnected = connected;
        Publish(State);
        PublishSummon();
    }

    public void SetPk(bool inPk)
    {
        if (pk == inPk) return;
        pk = inPk;
        Publish(State);
        PublishSummon();
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
        PublishSummon();
    }

    /// <summary>Only a running 期 is told: an ended one has no new first place.</summary>
    public void SetGiftBoard(GiftBoardState state)
    {
        if (!state.Running && giftBoard is null) return;
        giftBoard = state.Running ? state : null;
        Publish(State);
    }

    public void Tell(string name, object data) => Publish(new(name, data));

    /// <summary>Only who wrote it and whether it is the 帮它的关键字 saved last: what a viewer wrote never reaches the stream.</summary>
    public void TellDanmaku(Danmaku danmaku)
    {
        if (service.Settings is { } settings)
            Tell(Danmaku, new { danmaku.Uid, danmaku.Nickname, joins = Settings.SameKeyword(danmaku.Content, settings.DesktopPetPranksterKeyword.Value) });
    }

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

    /// <summary>A 试演 is over too once every page told it is gone, such as one the streaming software reloaded; so is a 来访.</summary>
    public void ConnectionsChanged()
    {
        if (trial is { } playing && Gone(playing.Pages)) Ended();
        if (Gone(visitPages)) (summoned, visiting) = (false, 0);
        PublishSummon();
    }

    /// <summary>叫它来: the pages have the 捣蛋鬼 come once her show is over. While it cannot, nothing is told them and the GUI is
    /// told why again.</summary>
    public void SummonPrankster()
    {
        if (SummonState.CanSummon)
        {
            (summoned, visitPages) = (true, Pages);
            Tell(Summon, new());
        }
        PublishSummon();
    }

    /// <summary>A page reports the 捣蛋鬼 set off, called or on its own: it is on the page until each page it set off on says it
    /// left.</summary>
    public void VisitStarted()
    {
        (summoned, visitPages) = (false, Pages);
        visiting++;
        PublishSummon();
    }

    /// <summary>A page reports the 捣蛋鬼 left, or never set off, as when a PK started while it was on its way.</summary>
    public void VisitEnded()
    {
        (summoned, visiting) = (false, Math.Max(0, visiting - 1));
        PublishSummon();
    }

    /// <summary>Why the streamer cannot 叫它来 now, by the 桌宠页's own rules: it has nowhere to come to, nobody to send it gifts or
    /// help it, or something on the page would scare it away at once.</summary>
    private string? WhyNoSummon() => !Enabled ? "先打开桌宠"
        : service.Status.DesktopPetConnections == 0 ? "先在直播姬里加好桌宠页"
        : !roomConnected ? "连上直播间才能叫它来：没人送礼，也没人能帮它"
        : Drawing(lottery) ? "抽完奖才能叫：抽奖会把它吓跑"
        : pk ? "PK 打完才能叫：PK 时它不出来" : null;

    /// <summary>A lottery she waits through, as the page's lotOf (engine.mjs) has it: entries open, or a draw coming (entries closed
    /// with someone to draw, a pool spinning or its draw on the way to its DrawUntil); a pool between draws is none. Whether the
    /// streamer turned the lottery's show off does not matter: it scares the 捣蛋鬼 away all the same.</summary>
    private static bool Drawing(Lottery? lottery) => lottery is EntryLottery { Phase: EntryLotteryPhase.Open }
        or EntryLottery { Phase: EntryLotteryPhase.Closed, EntryCount: > 0 } or PoolLottery { Phase: PoolLotteryPhase.Spinning }
        or PoolLottery { DrawUntil: not null };

    private void PublishSummon() => SummonSubscribers.Publish(SummonState);

    private bool Gone(WebService.Connection[] pages) => !pages.Intersect(Pages).Any();

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
/// start from, whether the streamer is in a PK, the lottery as it is on air, whose DrawUntil is when a draw is revealed, the 礼物榜
/// while its 期 runs, her settings and the current character's lines by kind, with the kinds the streamer changed.</summary>
internal sealed record DesktopPetState(bool Enabled, bool RoomConnected = false, bool Pk = false, Lottery? Lottery = null,
    GiftBoardState? GiftBoard = null, DesktopPetSettings? Settings = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? Lines = null);

/// <summary>How the streamer set the 桌宠 to play: the 演出表 rows turned off; the 中礼物 and 大礼物 thresholds, in 金瓜子 as the
/// rounds' amounts are; the quiet minutes before 无聊, 沮丧 and 犯困, 0 for never; the least and most seconds between 日常小动作; and
/// the percent chance one goes wrong; the most minutes between the 捣蛋鬼's own 来访, 0 for only when the streamer 叫它来; and the
/// 帮它的关键字, which its 招人 line says.</summary>
internal sealed record DesktopPetSettings(IReadOnlyList<string> Off, long MidGift, long BigGift, int BoredMinutes, int GloomyMinutes,
    int SleepyMinutes, int DailyMinSeconds, int DailyMaxSeconds, int MishapPercent, int PranksterMinutes, string PranksterKeyword)
{
    public static DesktopPetSettings Of(Settings settings) => new(settings.DesktopPetShowsOff.Value, Battery.ToCoins(settings.DesktopPetMidGift.Value),
        Battery.ToCoins(settings.DesktopPetBigGift.Value), settings.DesktopPetBoredMinutes.Value, settings.DesktopPetGloomyMinutes.Value,
        settings.DesktopPetSleepyMinutes.Value, settings.DesktopPetDailyMinSeconds.Value, settings.DesktopPetDailyMaxSeconds.Value,
        settings.DesktopPetMishapPercent.Value, settings.DesktopPetPranksterMinutes.Value, settings.DesktopPetPranksterKeyword.Value);
}

/// <summary>A viewer's 一轮送礼, told after each gift message with the viewer's gifts in it, and once more as it ends, with none:
/// the 桌宠页 makes her round of those it does not throw at the 捣蛋鬼. Its 大航海 play on their own and are in none of it.</summary>
internal sealed record GiftRoundProgress(long Uid, string Nickname, IReadOnlyList<GiftItem> Items, bool Ended);

/// <summary>One of a gift message's gifts: its gift id, name and price of one, a 盲盒's those of the gift it opened, by which the
/// 桌宠页 picks a round's gift priced highest and counts how many of it, as GiftRoundFeature's rounds did; its 投入 in 金瓜子, a
/// 盲盒's the box's price; how many; whether it is a 盲盒, and whether that opened a 浪漫城堡, the only way B站 gives one (one
/// sent as a gift is that gift); and its icon, a 盲盒's that of the gift it opened, empty when B站 gave none.</summary>
internal sealed record GiftItem(long GiftId, string Name, long Price, long Spend, long Count, bool Blind, bool Castle, string IconUrl)
{
    public static GiftItem Of(Gift gift) => new(gift.GiftId, gift.GiftName, gift.Price, gift.Spend, gift.Num, gift.Blind is not null,
        gift is { Blind: not null, IsCastle: true }, gift.IconUrl);
}

/// <summary>A viewer's 一轮盲盒 as its 盲盒播报 reported it, sent or given up, or without one as the 一轮送礼 ended: the tally, how
/// many boxes, the gift opened priced highest (one of it) and how many 浪漫城堡 came out, each a castle show of its own. One
/// given up is still told, as one without a 盲盒播报 is: she stops shaking the boxes only for their result.</summary>
internal sealed record BlindBoxResult(long Uid, string Nickname, BlindBoxTally Tally, long Boxes, string BestGift, long BestPrice, long Castles)
{
    public static BlindBoxResult Of(long uid, string nickname, IReadOnlyCollection<Gift> boxes)
    {
        var best = boxes.OrderByDescending(box => box.Price).First();
        return new(uid, nickname, BlindBoxTally.Of(boxes), boxes.Sum(box => box.Num), best.GiftName, best.Price,
            boxes.Where(box => box.IsCastle).Sum(box => box.Num));
    }
}
