namespace Aether.Core.Tests;

/// <summary>The 演出表 (#359) has every 演出 of the design package's README, sections 3, 4 and 6, with its 演出档, its kinds of lines
/// and what a 试演 of it can choose; Core, the GUI and the 桌宠页 all read this one table.</summary>
public sealed class ShowTableTests
{
    private static readonly ShowTable Table = ShowTable.Current;

    [Fact]
    public void It_has_each_show_daily_action_and_mood_of_the_design_with_its_tier()
    {
        Assert.Equal(
        [
            ("entry", "entry", "C"), ("gift", "gift", "C"), ("giftStreak", "gift", "C"), ("midGift", "gift", "B"), ("bigGift", "gift", "A"),
            ("blindBoxSmall", "blindBox", "C"), ("blindBoxMedium", "blindBox", "B"), ("blindBoxLarge", "blindBox", "A"), ("castle", "castle", "S"),
            ("captain", "guard", "B"), ("admiral", "guard", "A"), ("governor", "guard", "SS"), ("newFirst", "newFirst", "C"), ("lottery", "lottery", "A"),
            ("happy", "mood", "D"), ("calm", "mood", "D"), ("bored", "mood", "D"), ("gloomy", "mood", "D"), ("sleepy", "mood", "D"), ("wake", "mood", "D"),
            ("hum", "daily", "D"), ("gym", "daily", "D"), ("hike", "daily", "D"), ("bike", "daily", "D"), ("ski", "daily", "D"),
        ], Table.Shows.Select(show => (show.Id, show.Group, show.Tier)));
        Assert.Equal(["进场", "礼物", "盲盒", "城堡", "大航海", "新的第一名", "抽奖", "心情", "日常", "出事"], Table.Groups.Select(group => group.Label));
        // Moods come of quiet minutes, not of a switch.
        Assert.Equal(Table.Shows.Where(show => show.Group != ShowGroup.Mood), Table.Shows.Where(show => show.CanTurnOff));
    }

    [Fact]
    public void A_test_show_chooses_a_blind_boxs_ending_a_daily_actions_mishap_or_a_lotterys_step()
    {
        string?[] boxes = ["Gain", "Even", "Loss"], daily = [null, "Mishap"];
        Assert.Equal(
        [
            ("entry", [null]), ("gift", [null]), ("giftStreak", [null]), ("midGift", [null]), ("bigGift", [null]),
            ("blindBoxSmall", boxes), ("blindBoxMedium", boxes), ("blindBoxLarge", [.. boxes, "Jackpot"]), ("castle", [null]),
            ("captain", [null]), ("admiral", [null]), ("governor", [null]), ("newFirst", [null]),
            ("lottery", ["EntryOpen", "EntryClosed", "PoolOpen", "PoolCall", "PoolSpin", "RevealGrand", "RevealSecond", "RevealThird", "NoEntries", "End"]),
            ("happy", []), ("calm", []), ("bored", [null]), ("gloomy", [null]), ("sleepy", [null]), ("wake", []),
            ("hum", daily), ("gym", daily), ("hike", daily), ("bike", daily), ("ski", daily),
        ], Table.Shows.Select(show => (show.Id, (string?[])[.. show.Tests])));
        Assert.All(Table.Shows.SelectMany(show => show.Tests).OfType<string>(), variant => Assert.Contains(variant, Table.Variants.Keys));
    }

    [Fact]
    public void Each_kind_of_line_has_its_placeholders_and_is_said_in_a_show_of_the_table()
    {
        Assert.Equal(
        [
            ("entry", "entry", "昵称"), ("wake", "entry", ""), ("gift", "gift", "昵称 礼物 个数"), ("giftStreak", "gift", "昵称 个数"),
            ("midGift", "gift", "昵称 礼物 个数"), ("bigGift", "gift", "昵称 礼物 个数"), ("newFirst", "gift", "昵称"),
            ("blindBoxGain", "blindBox", "昵称 礼物 个数"), ("blindBoxEven", "blindBox", "昵称 个数"), ("blindBoxLoss", "blindBox", "昵称 个数"),
            ("blindBoxJackpot", "blindBox", "昵称 礼物"), ("captain", "guard", "昵称"), ("admiral", "guard", "昵称"), ("governor", "guard", "昵称"),
            ("castle", "castle", "昵称"), ("castleGate", "castle", "昵称"),
            ("lotteryEntryOpen", "lottery", "关键字"), ("lotteryEntryOngoing", "lottery", "关键字"), ("lotteryEntryClosed", "lottery", ""),
            ("lotteryWaiting", "lottery", ""), ("lotteryPoolOpen", "lottery", ""), ("lotteryPoolCall", "lottery", "昵称"), ("lotteryPoolSpin", "lottery", ""),
            ("lotteryGrand", "lottery", "昵称 奖项 奖品"), ("lotterySecond", "lottery", "昵称 奖项 奖品"), ("lotteryThird", "lottery", "昵称 奖项 奖品"),
            ("lotteryNoEntries", "lottery", ""), ("lotteryEnd", "lottery", ""),
            ("bored", "mood", ""), ("gloomy", "mood", ""), ("sleepy", "mood", ""),
            ("hum", "daily", ""), ("gym", "daily", ""), ("hike", "daily", ""), ("bike", "daily", ""), ("ski", "daily", ""),
            ("voiceCrack", "mishap", ""), ("droppedOnFoot", "mishap", ""), ("slipped", "mishap", ""), ("dustOff", "mishap", ""),
            ("bikeCrash", "mishap", ""), ("plaster", "mishap", ""), ("skiFall", "mishap", ""),
        ], Table.Lines.Select(kind => (kind.Id, kind.Group, string.Join(' ', kind.Placeholders))));
        Assert.Equal(Table.Lines.Select(kind => kind.Id).Order(), Table.Shows.SelectMany(show => show.Lines).Distinct().Order());
        Assert.All(Table.Shows.Select(show => show.Group).Concat(Table.Lines.Select(kind => kind.Group)),
            group => Assert.Contains(group, Table.Groups.Select(known => known.Id)));
    }

    [Theory]
    [InlineData("谢谢{昵称}的{礼物}！", null)]
    [InlineData("{昵称}送了{个数}个{礼物}…谢、谢谢！", null)]
    [InlineData("没有花括号以外的占位 { 也行", null)]
    [InlineData("开出{奖品}了", "这一类台词不能用 {奖品}，只能用 {昵称}、{礼物}、{个数}。")]
    [InlineData("  ", "每一句都不能为空。")]
    public void A_line_may_hold_only_its_kinds_placeholders(string line, string? error) =>
        Assert.Equal(error, Table.Line("gift").Check([line]));

    [Fact]
    public void A_list_of_lines_is_never_empty_and_a_kind_without_placeholders_says_so()
    {
        Assert.Equal("台词列表不能为空。", Table.Line("gift").Check([]));
        Assert.Equal("这一类台词不能用 {昵称}，没有可用的占位。", Table.Line("bored").Check(["好安静啊…", "{昵称}"]));
    }
}
