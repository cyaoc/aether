using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests;

public sealed class LotteryHistoryPanelTests
{
    [Fact]
    public Task Pool_history_keeps_the_latest_nonempty_nickname_for_each_uid() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var pool = LotteryPanelTests.TestLottery();
        vm.Lottery.SelectedLotteryHistory = pool with
        {
            Draws = [new("1", pool.Prizes[0], 42, "旧昵称", true, LotteryHistoryFixture.At, []),
                new("2", pool.Prizes[0], 42, "新昵称", true, LotteryHistoryFixture.At.AddMinutes(1), []),
                new("3", pool.Prizes[0], 42, "", true, LotteryHistoryFixture.At.AddMinutes(2), [])],
        };
        try { Assert.Equal("新昵称 ×3", Assert.Single(vm.Lottery.HistoryWinnerGroups[0].Winners).Name); }
        finally { await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(2026, "10月16日")]
    [InlineData(2027, "2026年10月16日")]
    public Task History_dates_use_the_view_model_clock(int year, string date) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await LotteryHistoryFixture.SeedAsync(h);
        var vm = h.CreateViewModel(new FakeTimeProvider(LotteryHistoryFixture.At.AddYears(year - 2026)));
        try
        {
            await vm.Lottery.RefreshLotteryHistoryAsync();
            var expected = $"{date} {LotteryHistoryFixture.At.ToLocalTime():HH:mm}";
            Assert.Equal(expected, vm.Lottery.LotteryHistoryRows[0].Date);
            Assert.Equal(expected, vm.Lottery.HistoryDetailDate);
        }
        finally { await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task History_switch_preserves_the_current_form_and_empty_history_is_read_only() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 816, Height = 600 };
        window.Show();
        try
        {
            var input = panel.FindControl<TextBox>("StopWordInput")!;
            input.Text = "还没保存的口令";
            var tabs = panel.FindControl<ListBox>("ViewTabs");
            Assert.NotNull(tabs);
            tabs.SelectedIndex = 1;
            await vm.Lottery.RefreshLotteryHistoryAsync();
            GuiHarness.Flush();
            Assert.False(panel.FindControl<Grid>("PoolContent")!.IsVisible);
            Assert.False(panel.FindControl<Border>("PhaseChip")!.IsVisible);
            Assert.Contains("暂无抽奖历史", GuiHarness.VisibleText(window));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), control => control.IsEffectivelyVisible);
            tabs.SelectedIndex = 0;
            GuiHarness.Flush();
            Assert.True(panel.FindControl<Grid>("PoolContent")!.IsVisible);
            Assert.Equal("还没保存的口令", input.Text);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Database_history_reopens_offline_and_selection_shows_both_modes_without_changing_current() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await LotteryHistoryFixture.SeedAsync(h);
        var vm = h.CreateViewModel(new FakeTimeProvider(LotteryHistoryFixture.At));
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var tabs = panel.FindControl<ListBox>("ViewTabs")!;
            tabs.SelectedIndex = 1;
            await vm.Lottery.RefreshLotteryHistoryAsync();
            GuiHarness.Flush();
            Assert.Equal("历史 2", panel.FindControl<TextBlock>("HistoryTabText")!.Text);
            var history = panel.FindControl<LotteryHistoryPanel>("HistoryContent")!;
            var list = history.FindControl<ListBox>("LotteryList")!;
            Assert.Equal(new[] { "entry", "pool" }, vm.Lottery.LotteryHistoryRows.Select(row => row.Id));
            Assert.Contains("已抽 2 / 3", GuiHarness.VisibleText(window));
            Assert.Contains("番茄炒蛋", GuiHarness.VisibleText(window));
            Assert.Contains("发弹幕「抽奖」", GuiHarness.VisibleText(window));
            var back = history.FindControl<Button>("ReturnButton")!;
            Assert.True(back.IsVisible);
            Assert.False(back.IsEnabled);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, LotteryHistoryFixture.Current)));
            await vm.Lottery.RefreshLotteryHistoryAsync();
            Assert.True(back.IsEnabled);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.True(panel.FindControl<EntryLotteryPanel>("EntryContent")!.IsVisible);
            tabs.SelectedIndex = 1;
            await vm.Lottery.RefreshLotteryHistoryAsync();
            list.SelectedIndex = 1;
            await GuiHarness.WaitForAsync(() => vm.Lottery.SelectedLotteryHistory?.Id == "pool" && !vm.Lottery.LotteryHistoryLoading);
            GuiHarness.Flush();
            Assert.Contains("抽了 7 次", GuiHarness.VisibleText(window));
            Assert.Contains("晚风 ×2", GuiHarness.VisibleText(window));
            Assert.Contains("UID 88", GuiHarness.VisibleText(window));
            Assert.Contains("主播点停", GuiHarness.VisibleText(window));
            Assert.Contains("口令「停」，每次点名一位观众喊停，或主播点停", GuiHarness.VisibleText(window));
            Assert.False(back.IsVisible);
            Assert.Equal("entry", vm.Lottery.LotteryState.Lottery!.Id);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), control => control.IsEffectivelyVisible);
            vm.Lottery.Apply(new LotteryUpdate(vm.Lottery.LotteryState with { Lottery = LotteryHistoryFixture.Current with { Hidden = true } }));
            await vm.Lottery.RefreshLotteryHistoryAsync();
            Assert.Equal("pool", vm.Lottery.SelectedLotteryHistory!.Id);
            list.SelectedIndex = 0;
            await vm.Lottery.RefreshLotteryHistoryAsync();
            Assert.Equal("entry", vm.Lottery.SelectedLotteryHistory!.Id);
            vm.Lottery.Apply(new LotteryUpdate(new(999, true, null)));
            await vm.Lottery.RefreshLotteryHistoryAsync();
            Assert.Equal("other", Assert.Single(vm.Lottery.LotteryHistory).Id);
            Assert.Equal("other", vm.Lottery.SelectedLotteryHistory!.Id);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
