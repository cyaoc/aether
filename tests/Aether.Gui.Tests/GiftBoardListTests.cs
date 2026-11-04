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
        scene.Give(PaidGift(1, "柚子不酸", "天空之翼", 1000), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5), PaidGift(3, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 3);

        Assert.Equal(["1 月下Lily 次元之城 ×1 12450 电池", "2 龍騰雲海 告白气球 ×5 10000 电池", "3 柚子不酸 天空之翼 ×1 1000 电池"], scene.Listed());
        Assert.Equal((await scene.StateAsync()).Rows.Select(row => row.Nickname), scene.Listed().Select(line => line.Split(' ')[1]));
    }, On);

    [Fact]
    public Task The_list_counts_its_rows_and_says_when_the_period_began() => Run(async scene =>
    {
        scene.GoToToday();
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "龍騰雲海", "告白气球", 2000, 5));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 2);

        Assert.Equal("共 2 位", scene.Page.FindControl<TextBlock>("BoardCount")!.Text);
        Assert.Equal("本期从 11月4日 20:03 开始", scene.Page.FindControl<TextBlock>("BoardPeriod")!.Text);
    }, On);

    [Theory]
    [InlineData(false, true, "连上直播间后，这里显示这个直播间的礼物榜。")]
    [InlineData(true, false, "礼物榜关着，不记录也不显示。")]
    [InlineData(true, true, "还没有人上榜。达到门槛的礼物会出现在这里和直播画面上。")]
    public Task An_empty_list_says_why_it_is_empty(bool connected, bool enabled, string text) => Run(scene =>
    {
        if (connected) scene.Connect();
        GuiHarness.Flush();

        var empty = scene.Page.FindControl<TextBlock>("BoardEmpty")!;
        Assert.True(empty.IsEffectivelyVisible);
        Assert.Equal(text, empty.Text);
        Assert.Empty(scene.Page.FindControl<TextBlock>("BoardCount")!.Text!);
        Assert.False(scene.Page.FindControl<TextBlock>("BoardPeriod")!.IsEffectivelyVisible);
        return Task.CompletedTask;
    }, [("gift_board.enabled", enabled ? "true" : "false")]);

    [Fact]
    public Task The_list_follows_the_saved_comparison_and_not_the_unsaved_one() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450), PaidGift(2, "星屑Koi", "干杯", 66, 8));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 1);

        scene.Page.FindControl<RadioButton>("ByTotalPriceInput")!.IsChecked = true;
        GuiHarness.Flush();
        Assert.Single(scene.Listed());

        GuiHarness.FindVisual<Button>(scene.Page, "PART_SaveButton").RaiseEvent(new(Button.ClickEvent));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 2);
        Assert.Equal(["1 月下Lily 次元之城 ×1 12450 电池", "2 星屑Koi 干杯 ×8 528 电池"], scene.Listed());
    }, [.. On, ("gift_board.threshold", "100")]);

    [Fact]
    public Task The_list_stays_as_it_is_while_the_room_reconnects_and_empties_when_the_room_is_left() => Run(async scene =>
    {
        scene.Connect();
        scene.Give(PaidGift(1, "月下Lily", "次元之城", 12450));
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 1);

        scene.ViewModel.ConnectionState = RoomConnectionState.Reconnecting;
        GuiHarness.Flush();
        Assert.Single(scene.Listed());
        Assert.False(scene.Page.FindControl<TextBlock>("BoardEmpty")!.IsEffectivelyVisible);

        scene.Disconnect();
        await GuiHarness.WaitForAsync(() => scene.Listed().Length == 0);
        Assert.Equal("连上直播间后，这里显示这个直播间的礼物榜。", scene.Page.FindControl<TextBlock>("BoardEmpty")!.Text);
    }, On);
}
