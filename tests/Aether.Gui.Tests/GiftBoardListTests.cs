using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using static Aether.Gui.Tests.Support.GiftBoardScene;

namespace Aether.Gui.Tests;

public sealed class GiftBoardListTests
{
    [Fact]
    public Task The_list_has_the_boards_rows_in_the_boards_order_with_their_value_in_batteries() => Run(async scene =>
    {
        scene.Connect();
        scene.Start();
        scene.Give(PaidGift(1, "柚子不酸", "天空之翼", 1000), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5), PaidGift(3, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 3);

        Assert.Equal(["1 月下Lily 次元之城 ×1 12450 电池", "2 龍騰雲海 告白气球 ×5 10000 电池", "3 柚子不酸 天空之翼 ×1 1000 电池"], scene.Listed());
        Assert.Equal((await scene.Board.CurrentAsync()).Rows.Select(row => row.Nickname), scene.Listed().Select(line => line.Split(' ')[1]));
    });

    [Fact]
    public Task The_list_counts_its_viewers_and_says_when_the_period_began() => Run(async scene =>
    {
        scene.GoToToday();
        scene.Connect();
        scene.Start();
        // 月下Lily is on the board twice, once for each gift.
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5), PaidGift(1, "月下Lily", "告白气球", 2000));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 3);

        Assert.Equal("共 2 位", scene.Page.FindControl<TextBlock>("BoardCount")!.Text);
        Assert.Equal("本期从 11月4日 20:03 开始", scene.Page.FindControl<TextBlock>("BoardPeriod")!.Text);
    });

    [Theory]
    [InlineData(false, false, false, "连上直播间后，这里显示这个直播间的礼物榜。")]
    [InlineData(true, false, false, "还没有开始统计。点右上角“开始新一期”，之后收到的礼物会出现在这里和直播画面上。")]
    [InlineData(true, true, false, "还没有人上榜。达到门槛的礼物会出现在这里和直播画面上。")]
    [InlineData(true, true, true, "这一期没有人上榜。")]
    public Task An_empty_list_says_why_it_is_empty(bool connected, bool started, bool ended, string text) => Run(async scene =>
    {
        if (connected) scene.Connect();
        if (started) scene.Start();
        if (ended) scene.Board.EndPeriod(RoomId);

        var empty = scene.Page.FindControl<TextBlock>("BoardEmpty")!;
        await GuiHarness.WaitForAsync(() => empty.Text == text);
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Empty(scene.Page.FindControl<TextBlock>("BoardCount")!.Text!);
        // Only a 期 has a time to tell.
        Assert.Equal(started, scene.Page.FindControl<TextBlock>("BoardPeriod")!.IsEffectivelyVisible);
    });

    [Fact]
    public Task The_list_follows_the_comparison_as_soon_as_it_is_picked() => Run(async scene =>
    {
        scene.Connect();
        scene.Start(threshold: 100);
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "星屑Koi", "干杯", 66, 8));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 1);

        scene.Page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked = true;

        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 2);
        Assert.Equal(["1 月下Lily 次元之城 ×1 12450 电池", "2 星屑Koi 干杯 ×8 528 电池"], scene.Listed());
    });

    [Fact]
    public Task The_list_stays_as_it_is_while_the_room_reconnects_and_empties_when_the_room_is_left() => Run(async scene =>
    {
        scene.Connect();
        scene.Start();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 1);

        scene.ViewModel.ConnectionState = RoomConnectionState.Reconnecting;
        GuiHarness.Flush();
        Assert.Single(scene.Listed());
        Assert.False(scene.Page.FindControl<TextBlock>("OfflineHint")!.IsEffectivelyVisible);
        Assert.True(scene.Page.FindControl<Button>("EndButton")!.IsEffectivelyVisible);

        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 0);
        Assert.Equal("连上直播间后，这里显示这个直播间的礼物榜。", scene.Page.FindControl<TextBlock>("BoardEmpty")!.Text);
    });
}
