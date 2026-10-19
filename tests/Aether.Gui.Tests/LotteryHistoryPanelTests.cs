using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class LotteryHistoryPanelTests
{
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
            await vm.RefreshLotteryHistoryAsync();
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
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var tabs = panel.FindControl<ListBox>("ViewTabs")!;
            tabs.SelectedIndex = 1;
            await vm.RefreshLotteryHistoryAsync();
            GuiHarness.Flush();
            Assert.Equal("历史 2", panel.FindControl<TextBlock>("HistoryTabText")!.Text);
            var history = panel.FindControl<LotteryHistoryPanel>("HistoryContent")!;
            var list = history.FindControl<ListBox>("LotteryList")!;
            Assert.Equal(new[] { "entry", "pool" }, list.Items.OfType<ListBoxItem>().Select(row => ((LotteryHistory)row.Tag!).Id));
            Assert.Contains("已抽 2 / 3", GuiHarness.VisibleText(window));
            Assert.Contains("番茄炒蛋", GuiHarness.VisibleText(window));
            Assert.Contains("发弹幕「抽奖」", GuiHarness.VisibleText(window));
            var back = history.FindControl<Button>("ReturnButton")!;
            Assert.True(back.IsVisible);
            Assert.False(back.IsEnabled);
            vm.Apply(new LotteryUpdate(new(7734200, true, LotteryHistoryFixture.Current)));
            await vm.RefreshLotteryHistoryAsync();
            Assert.True(back.IsEnabled);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, tabs.SelectedIndex);
            Assert.True(panel.FindControl<EntryLotteryPanel>("EntryContent")!.IsVisible);
            tabs.SelectedIndex = 1;
            await vm.RefreshLotteryHistoryAsync();
            list.SelectedIndex = 1;
            await GuiHarness.WaitForAsync(() => vm.SelectedLotteryHistory?.Id == "pool" && !vm.LotteryHistoryLoading);
            GuiHarness.Flush();
            Assert.Contains("抽了 7 次", GuiHarness.VisibleText(window));
            Assert.Contains("晚风 ×2", GuiHarness.VisibleText(window));
            Assert.Contains("UID 88", GuiHarness.VisibleText(window));
            Assert.Contains("主播点停", GuiHarness.VisibleText(window));
            Assert.Contains("口令「停」，每次点名一位观众喊停，或主播点停", GuiHarness.VisibleText(window));
            Assert.False(back.IsVisible);
            Assert.Equal("entry", vm.LotteryState.Lottery!.Id);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), control => control.IsEffectivelyVisible);
            vm.Apply(new LotteryUpdate(vm.LotteryState with { Lottery = LotteryHistoryFixture.Current with { Hidden = true } }));
            await vm.RefreshLotteryHistoryAsync();
            Assert.Equal("pool", vm.SelectedLotteryHistory!.Id);
            list.SelectedIndex = 0;
            await vm.RefreshLotteryHistoryAsync();
            Assert.Equal("entry", vm.SelectedLotteryHistory!.Id);
            vm.Apply(new LotteryUpdate(new(999, true, null)));
            await vm.RefreshLotteryHistoryAsync();
            Assert.Equal("other", Assert.Single(vm.LotteryHistory).Id);
            Assert.Equal("other", vm.SelectedLotteryHistory!.Id);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
