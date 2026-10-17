using System.Collections.Immutable;
using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class PoolLotteryPageTests
{
    [Fact]
    public Task Draft_survives_connecting_and_validation_and_ten_prize_limit_apply() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var page = new LotteryPage { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var rows = page.FindControl<StackPanel>("PrizeRows")!;
            var first = Assert.IsType<PoolPrizeRow>(rows.Children[0]);
            first.FindControl<TextBox>("NameInput")!.Text = "手办";
            GuiHarness.Flush();
            vm.Apply(new PoolLotteryUpdate(new(7734200, true, null)));
            Assert.Equal("手办", Assert.IsType<PoolPrizeRow>(rows.Children[0]).FindControl<TextBox>("NameInput")!.Text);
            var start = page.FindControl<Button>("StartButton")!;
            Assert.True(start.IsEnabled);
            var uid = page.FindControl<TextBox>("UidInput")!;
            uid.Focus();
            foreach (var invalid in new[] { "0", "-1", "abc", "1.5", "9223372036854775808" })
            {
                uid.Text = invalid;
                GuiHarness.Flush();
                Assert.False(start.IsEnabled);
            }
            Assert.False(page.FindControl<TextBlock>("UidError")!.IsVisible);
            page.FindControl<TextBox>("StopWordInput")!.Focus();
            Assert.True(page.FindControl<TextBlock>("UidError")!.IsVisible);
            uid.Text = "42";
            GuiHarness.Flush();
            Assert.Contains("还没发过", page.FindControl<TextBlock>("ViewerHint")!.Text);
            page.FindControl<TextBox>("StopWordInput")!.Text = " ";
            GuiHarness.Flush();
            Assert.False(start.IsEnabled);
            page.FindControl<TextBox>("StopWordInput")!.Text = "停";
            GuiHarness.Flush();
            Assert.True(start.IsEnabled);
            first.FindControl<NumericUpDown>("WeightInput")!.Text = "1.5";
            GuiHarness.Flush();
            Assert.False(start.IsEnabled);
            first.FindControl<NumericUpDown>("WeightInput")!.Text = "1";
            GuiHarness.Flush();
            var add = page.FindControl<Button>("AddButton")!;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var added = Assert.IsType<PoolPrizeRow>(rows.Children[1]);
            Assert.False(added.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            Assert.False(start.IsEnabled);
            uid.Focus();
            Assert.True(added.FindControl<TextBlock>("ErrorMessage")!.IsVisible);
            added.FindControl<TextBox>("NameInput")!.Text = "贴纸";
            GuiHarness.Flush();
            added.FindControl<NumericUpDown>("WeightInput")!.Text = "3";
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

    internal static PoolLotterySession Session(PoolLotteryPhase phase = PoolLotteryPhase.Idle) =>
        new("test-lottery", 7734200, "停", DateTimeOffset.UnixEpoch, null,
            [new("限定抱枕", 1, PrizeGrade.Grand), new("贴纸", 9, PrizeGrade.Third)], [],
            ImmutableDictionary<long, string>.Empty.Add(42, "小明"), phase, 42);

    [Fact]
    public Task Core_state_controls_buttons_and_viewer_hint() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var page = new LotteryPage { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            Assert.False(page.FindControl<Button>("StartButton")!.IsEnabled);
            vm.Apply(new PoolLotteryUpdate(new(7734200, true, Session())));
            GuiHarness.Flush();
            Assert.True(page.FindControl<Button>("StartButton")!.IsEnabled);
            Assert.Contains("小明", page.FindControl<TextBlock>("ViewerHint")!.Text);
            vm.Apply(new PoolLotteryUpdate(new(7734200, true, Session(PoolLotteryPhase.Spinning))));
            Assert.True(page.FindControl<Button>("StopButton")!.IsVisible);
            Assert.True(page.FindControl<Button>("CancelButton")!.IsVisible);
            Assert.False(page.FindControl<StackPanel>("PrizeRows")!.IsEnabled);
            vm.Apply(new PoolLotteryUpdate(new(7734200, true, Session(PoolLotteryPhase.Revealing))));
            Assert.True(page.FindControl<Button>("RevealingButton")!.IsVisible);
            Assert.False(page.FindControl<Button>("RevealingButton")!.IsEnabled);
            Assert.False(page.FindControl<Button>("NewButton")!.IsEnabled);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
