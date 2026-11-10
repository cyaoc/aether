namespace Aether.Core;

/// <summary>What the 桌宠页 is told, in the web service region (ADR 0017): a state first and whenever it changes, then each thing
/// that happens as one event. The page decides what to play; while the 桌宠 is off it is told only that.</summary>
internal sealed class DesktopPet(WebService service)
{
    /// <summary>What happens, by the name the page is told it under.</summary>
    public const string Gift = "gift", BlindBox = "blindBox", Guard = "guard", Entry = "entry", Danmaku = "danmaku",
        Test = "testShow", StopTest = "stopTestShow";

    private bool roomConnected;
    private Lottery? lottery;
    private GiftBoardState? giftBoard;

    public Subscribers<SseEvent> Subscribers { get; } = new();

    private bool Enabled => service.Settings?.DesktopPetEnabled.Value == true;

    public SseEvent State => new("state", Enabled ? new DesktopPetState(true, roomConnected, lottery, giftBoard) : new DesktopPetState(false));

    /// <summary>New settings may turn the 桌宠 on or off.</summary>
    public void Applied() => Subscribers.Publish(State);

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

    /// <summary>Has the page play <paramref name="show"/> now; refused with why while no page would.</summary>
    public void TestShow(string show, string? variant, string nickname)
    {
        var refused = !Enabled ? "桌宠没有打开。" : WebService.WhyNotRunning(service.Status)
            ?? (service.Status.DesktopPetConnections == 0 ? "桌宠页没有推流软件连着。" : null);
        if (refused is not null) throw new InvalidOperationException(refused);
        Tell(Test, new { show, variant, nickname });
    }

    public void StopTestShow() => Tell(StopTest, new());

    private void Publish(SseEvent message)
    {
        if (Enabled) Subscribers.Publish(message);
    }
}

/// <summary>The 桌宠页's state: whether the 桌宠 is on and, only then, whether a room is connected, which her quiet-room clocks
/// start from, the lottery as it is on air, whose DrawUntil is when a draw is revealed, and the 礼物榜 while its 期 runs.</summary>
internal sealed record DesktopPetState(bool Enabled, bool RoomConnected = false, Lottery? Lottery = null, GiftBoardState? GiftBoard = null);

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

/// <summary>A viewer's 一轮盲盒 as its 盲盒播报 reported it, or without one as the 一轮送礼 ended: the tally, how many boxes, the
/// gift opened priced highest (one of it) and whether a 浪漫城堡 came out.</summary>
internal sealed record BlindBoxResult(long Uid, string Nickname, BlindBoxTally Tally, long Boxes, string BestGift, long BestPrice, bool Castle)
{
    public static BlindBoxResult Of(long uid, string nickname, IReadOnlyCollection<Gift> boxes)
    {
        var best = boxes.OrderByDescending(box => box.Price).First();
        return new(uid, nickname, BlindBoxTally.Of(boxes), boxes.Sum(box => box.Num), best.GiftName, best.Price, boxes.Any(box => box.IsCastle));
    }
}
