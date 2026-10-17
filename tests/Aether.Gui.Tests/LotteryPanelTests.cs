using System.Collections.Immutable;
using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class LotteryPanelTests
{
    [Fact]
    public Task Draft_survives_connecting_and_validation_and_ten_prize_limit_apply() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var rows = page.FindControl<StackPanel>("PrizeRows")!;
            var first = Assert.IsType<PoolPrizeRow>(rows.Children[0]);
            first.FindControl<TextBox>("NameInput")!.Text = "手办";
            GuiHarness.Flush();
            vm.Apply(new LotteryUpdate(new(7734200, true, null)));
            Assert.Equal("手办", Assert.IsType<PoolPrizeRow>(rows.Children[0]).FindControl<TextBox>("NameInput")!.Text);
            var start = page.FindControl<Button>("StartButton")!;
            Assert.True(start.IsEnabled);
            var uid = page.FindControl<TextBox>("UidInput")!;
            var uidError = page.FindControl<TextBlock>("UidError")!;
            // Single inputs check as you type (#136): no need to leave the box first.
            foreach (var invalid in new[] { "0", "-1", "abc", "1.5", "9223372036854775808" })
            {
                uid.Text = invalid;
                GuiHarness.Flush();
                Assert.False(start.IsEnabled);
                Assert.True(uidError.IsVisible);
                Assert.Contains("invalid", uid.Classes);
            }
            uid.Text = "42";
            GuiHarness.Flush();
            Assert.False(uidError.IsVisible);
            Assert.Contains("还没发过", page.FindControl<TextBlock>("ViewerHint")!.Text);
            Assert.Contains("只认 UID 42 发的「停」", page.FindControl<TextBlock>("StatusText")!.Text);
            var word = page.FindControl<TextBox>("StopWordInput")!;
            word.Text = " ";
            GuiHarness.Flush();
            Assert.False(start.IsEnabled);
            Assert.True(page.FindControl<TextBlock>("StopWordError")!.IsVisible);
            word.Text = "停";
            GuiHarness.Flush();
            Assert.False(page.FindControl<TextBlock>("StopWordError")!.IsVisible);
            Assert.True(start.IsEnabled);
            var weight = first.FindControl<TextBox>("WeightInput")!;
            weight.Text = "1.5";
            GuiHarness.Flush();
            Assert.False(start.IsEnabled);
            weight.Text = "1";
            GuiHarness.Flush();
            var add = page.FindControl<Button>("AddButton")!;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var added = Assert.IsType<PoolPrizeRow>(rows.Children[1]);
            Assert.False(added.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.False(start.IsEnabled);
            uid.Focus();
            Assert.True(added.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.Contains("invalid", added.FindControl<TextBox>("NameInput")!.Classes);
            Assert.DoesNotContain("invalid", added.FindControl<TextBox>("WeightInput")!.Classes);
            added.FindControl<TextBox>("NameInput")!.Text = "贴纸";
            GuiHarness.Flush();
            added.FindControl<TextBox>("WeightInput")!.Text = "3";
            GuiHarness.Flush();
            Assert.Equal("25%", first.FindControl<TextBlock>("Percentage")!.Text);
            Assert.Equal("75%", added.FindControl<TextBlock>("Percentage")!.Text);
            for (var index = 2; index < 10; index++) add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(10, rows.Children.Count);
            Assert.False(add.IsEnabled);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal(10, rows.Children.Count);
            added.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(add.IsEnabled);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    internal static Lottery TestLottery(PoolLotteryPhase phase = PoolLotteryPhase.Idle) =>
        new("test-lottery", 7734200, "停", DateTimeOffset.UnixEpoch,
            [new("限定抱枕", 1, PrizeGrade.Grand), new("贴纸", 9, PrizeGrade.Third)], [],
            ImmutableDictionary<long, string>.Empty.Add(42, "小明"), phase, 42);

    [Fact]
    public Task Core_state_controls_buttons_and_viewer_hint() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var status = page.FindControl<TextBlock>("StatusText")!;
            Assert.False(page.FindControl<Button>("StartButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.Equal("先连直播间，才能开始抽奖", status.Text);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery())));
            GuiHarness.Flush();
            Assert.True(page.FindControl<Button>("StartButton")!.IsEnabled);
            Assert.True(page.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.Equal("小明 · 这场发过弹幕", page.FindControl<TextBlock>("ViewerHint")!.Text);
            Assert.Equal("待机", page.FindControl<TextBlock>("PhaseText")!.Text);
            Assert.False(page.FindControl<Border>("LatestCard")!.IsVisible);
            Assert.True(page.FindControl<TextBlock>("EmptyHistory")!.IsVisible);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery(PoolLotteryPhase.Spinning))));
            Assert.True(page.FindControl<Button>("StopButton")!.IsEnabled);
            Assert.True(page.FindControl<Button>("CancelButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("StartButton")!.IsVisible);
            Assert.False(page.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("AddButton")!.IsEffectivelyVisible);
            Assert.True(page.FindControl<TextBox>("UidInput")!.IsReadOnly);
            Assert.Equal("正在等 小明 发「停」，也可以直接点“停”", status.Text);
            Assert.Equal("转动中", vm.LotteryPhaseText);
            vm.Apply(new LotteryUpdate(new(7734200, false, TestLottery(PoolLotteryPhase.Spinning)) { CanControl = false }));
            Assert.False(page.FindControl<Button>("StopButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("CancelButton")!.IsEnabled);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery(PoolLotteryPhase.Drawing))));
            Assert.True(page.FindControl<Button>("DrawingButton")!.IsVisible);
            Assert.False(page.FindControl<Button>("DrawingButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("StopButton")!.IsVisible);
            Assert.False(page.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.Equal("开奖中", page.FindControl<TextBlock>("PhaseText")!.Text);
            var draw = new PoolDraw("draw", new("限定抱枕", 1, PrizeGrade.Grand), 42, "小明", true, DateTimeOffset.UnixEpoch, []);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery(PoolLotteryPhase.Drawing) with { Draws = [draw] })));
            Assert.True(page.FindControl<Border>("LatestCard")!.IsVisible);
            Assert.StartsWith("小明 · 主播点停 · ", page.FindControl<TextBlock>("LatestHead")!.Text);
            Assert.Equal("限定抱枕", page.FindControl<TextBlock>("LatestPrize")!.Text);
            Assert.Equal("本场 1 次", page.FindControl<TextBlock>("DrawCount")!.Text);
            Assert.Contains("fresh", Assert.Single(page.FindControl<StackPanel>("HistoryRows")!.Children).Classes);
            Assert.False(page.FindControl<TextBlock>("EmptyHistory")!.IsVisible);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery() with { Draws = [draw] })));
            Assert.DoesNotContain("fresh", Assert.Single(page.FindControl<StackPanel>("HistoryRows")!.Children).Classes);
            // Core carries the previous pool's UID, including when an entry lottery ran in between.
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery() with { Id = "next", Uid = 42 })));
            Assert.Equal("42", page.FindControl<TextBox>("UidInput")!.Text);
            vm.Apply(new LotteryUpdate(new(7734200, true, TestLottery() with { Id = "without-viewer", Uid = null })));
            Assert.Equal("", page.FindControl<TextBox>("UidInput")!.Text);
            Assert.Equal("", vm.LotteryPhaseText);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
