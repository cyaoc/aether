using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class EntryLotteryPanelTests
{
    internal static EntryLottery TestLottery(EntryLotteryPhase phase = EntryLotteryPhase.Preparing) =>
        new("entry", 7734200, "抽奖", DateTimeOffset.UnixEpoch,
            [new("一等奖", "机械键盘", 1, Grade.Grand), new("二等奖", "周边抱枕", 2, Grade.Second),
                new("三等奖", "签名照", 3, Grade.Third)], [], [], phase);

    [Fact]
    public Task Invalid_award_cannot_be_saved_and_reports_the_start_validation_message() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery())));
        var page = new EntryLotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            var picker = page.FindControl<LotteryPrizeSetPicker>("EntryPrizeSets")!;
            vm.Lottery.Awards[0].Capacity = "0";
            var save = picker.FindControl<Button>("SaveButton")!;
            save.Flyout!.ShowAt(save);
            picker.FindControl<TextBox>("NameInput")!.Text = "无效方案";
            GuiHarness.Flush();
            picker.FindControl<Button>("ConfirmButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => vm.Lottery.EntryLotteryError is not null && !vm.Lottery.Busy);
            Assert.Equal("名额必须是正整数。", page.FindControl<TextBlock>("ErrorMessage")!.Text);
            Assert.Empty(vm.Lottery.PrizeSets);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Entry_prize_sets_replace_only_awards_and_use_the_shared_picker() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, LotteryPanelTests.TestLottery())));
        await vm.Lottery.SavePrizeSetAsync(LotteryMode.Pool, "奖池方案");
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery())));
        var page = new EntryLotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            var picker = page.FindControl<LotteryPrizeSetPicker>("EntryPrizeSets");
            Assert.NotNull(picker);
            var save = picker.FindControl<Button>("SaveButton")!;
            save.Flyout!.ShowAt(save);
            var name = picker.FindControl<TextBox>("NameInput")!;
            var confirm = picker.FindControl<Button>("ConfirmButton")!;
            Assert.False(confirm.IsEnabled);
            name.Text = "报名方案";
            GuiHarness.Flush();
            Assert.Equal("存下这 3 个奖项，名称、奖品、名额和档次", picker.FindControl<TextBlock>("SaveHint")!.Text);
            Assert.Equal(30, save.Bounds.Height);
            Assert.True(picker.Bounds.Height >= save.Bounds.Height);
            Assert.Equal(320, confirm.GetVisualAncestors().OfType<FlyoutPresenter>().Single().Bounds.Width);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => vm.Lottery.PrizeSets.Count == 2 && !vm.Lottery.Busy);
            vm.Lottery.Awards[0].Prize = "限定手办";
            save.Flyout.ShowAt(save);
            GuiHarness.Flush();
            Assert.Equal("替换", confirm.Content);
            Assert.Equal("已有同名方案，保存会替换它", picker.FindControl<TextBlock>("SaveHint")!.Text);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => vm.Lottery.PrizeSets.Single(set => set.Mode == LotteryMode.Entry).Awards[0].Prize == "限定手办" && !vm.Lottery.Busy);
            vm.Lottery.Awards[0].Prize = "修改草稿";
            vm.Lottery.Keyword = "报名参加";
            var load = picker.FindControl<Button>("LoadButton")!;
            load.Flyout!.ShowAt(load);
            GuiHarness.Flush();
            var menu = (MenuFlyout)load.Flyout;
            var item = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Tag is LotteryPrizeSet);
            var set = Assert.IsType<LotteryPrizeSet>(item.Tag);
            Assert.Equal("报名方案", set.Name);
            Assert.Contains(Assert.IsType<Grid>(item.Header).GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "限定手办 ×1、周边抱枕 ×2、签名照 ×3");
            var presenter = item.GetVisualAncestors().OfType<MenuFlyoutPresenter>().Single();
            Assert.Equal(340, presenter.Bounds.Width);
            Assert.Equal(picker.TranslatePoint(new Point(picker.Bounds.Width, 0), window)!.Value.X,
                presenter.TranslatePoint(new Point(presenter.Bounds.Width, 0), window)!.Value.X, 1);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(set.Awards.ToArray(), vm.Lottery.Awards.Select(row => row.Value));
            Assert.Equal("报名参加", vm.Lottery.Keyword);
            Assert.Equal("机械键盘", Assert.IsType<EntryLottery>(vm.Lottery.LotteryState.Lottery).Awards[0].Prize);
            Assert.Contains("已载入「报名方案」", vm.Lottery.EntryStatusText);
            var first = vm.Lottery.Awards[0];
            GuiHarness.FindRowControl<TextBox>(page, first, "PrizeInput").Text = "";
            GuiHarness.Flush();
            Assert.True(GuiHarness.FindRowControl<TextBlock>(page, first, "ErrorMessage").IsVisible);
            load.Flyout.ShowAt(load);
            GuiHarness.Flush();
            item = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Tag is LotteryPrizeSet);
            Assert.Equal(Color.Parse("#E6EBFA"), Assert.IsAssignableFrom<ISolidColorBrush>(item.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PART_LayoutRoot").Background).Color);
            Assert.IsType<Grid>(item.Header).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await GuiHarness.WaitForAsync(() => vm.Lottery.PrizeSets.All(set => set.Mode != LotteryMode.Entry) && !vm.Lottery.Busy);
            Assert.Equal(LotteryMode.Pool, Assert.Single(vm.Lottery.PrizeSets).Mode);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery(EntryLotteryPhase.Open))));
            Assert.False(load.IsEnabled);
            Assert.False(save.IsEnabled);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Award_accessible_names_follow_rows_after_removing_the_first() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery() with { Awards = [TestLottery().Awards[0]] })));
            var entry = panel.FindControl<EntryLotteryPanel>("EntryContent")!;
            var rows = vm.Lottery.Awards;
            var add = entry.FindControl<Button>("AddAwardButton")!;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            Assert.Equal(3, rows.Count);
            var first = rows[0];
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(entry, first, "RemoveButton"))).Invoke();
            GuiHarness.Flush();
            var remaining = rows.ToArray();
            Assert.Equal(new[] { "第 1 个奖项名称", "第 2 个奖项名称" },
                remaining.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<TextBox>(entry, row, "NameInput"))));
            Assert.Equal(new[] { "第 1 个奖项的奖品", "第 2 个奖项的奖品" },
                remaining.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<TextBox>(entry, row, "PrizeInput"))));
            Assert.Equal(new[] { "第 1 个奖项名额", "第 2 个奖项名额" },
                remaining.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<TextBox>(entry, row, "CapacityInput"))));
            Assert.Equal(new[] { "第 1 个奖项档次", "第 2 个奖项档次" },
                remaining.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<GradePicker>(entry, row, "GradeInput"))));
            Assert.Equal(new[] { "删除第 1 个奖项", "删除第 2 个奖项" },
                remaining.Select(row => AutomationProperties.GetName(GuiHarness.FindRowControl<Button>(entry, row, "RemoveButton"))));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Phase_controls_editing_buttons_and_lists() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var lottery = TestLottery();
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, false, lottery)));
            var entry = panel.FindControl<EntryLotteryPanel>("EntryContent")!;
            var start = entry.FindControl<Button>("StartEntryButton")!;
            Assert.False(start.IsEnabled);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery)));
            Assert.True(start.IsEnabled);
            Assert.Equal("报名抽奖", panel.FindControl<TextBlock>("ModeText")!.Text);
            Assert.Equal("准备中", panel.FindControl<TextBlock>("PhaseText")!.Text);
            var keyword = entry.FindControl<TextBox>("KeywordInput")!;
            keyword.Text = " ";
            GuiHarness.Flush();
            Assert.False(start.IsEnabled);
            Assert.True(entry.FindControl<TextBlock>("KeywordError")!.IsVisible);
            Assert.Equal(LotteryController.ValidateKeyword(keyword.Text), entry.FindControl<TextBlock>("KeywordError")!.Text);
            keyword.Text = "抽奖";
            GuiHarness.Flush();
            Assert.False(entry.FindControl<TextBlock>("KeywordError")!.IsVisible);
            Assert.Equal(LotteryController.ValidateKeyword(keyword.Text), entry.FindControl<TextBlock>("KeywordError")!.Text);
            lottery = lottery with { Phase = EntryLotteryPhase.Open, Entries = [new(42, "小明", DateTimeOffset.UnixEpoch)] };
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery)));
            Assert.True(keyword.IsReadOnly);
            Assert.False(start.IsVisible);
            Assert.True(entry.FindControl<Button>("CloseEntriesButton")!.IsEnabled);
            Assert.False(panel.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.Equal("先结束这一场才能新开", panel.FindControl<TextBlock>("NewHint")!.Text);
            var row = vm.Lottery.Awards[0];
            Assert.False(GuiHarness.FindRowControl<ListBox>(entry, row, "GradeInput").IsVisible);
            Assert.True(GuiHarness.FindRowControl<Border>(entry, row, "GradeBadge").IsVisible);
            Assert.False(GuiHarness.FindRowControl<Button>(entry, row, "DrawButton").IsEnabled);
            lottery = lottery with { Phase = EntryLotteryPhase.Closed };
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery)));
            Assert.True(GuiHarness.FindRowControl<Button>(entry, row, "DrawButton").IsEnabled);
            Assert.True(entry.FindControl<Button>("EndEntryButton")!.IsEnabled);
            Assert.Equal("开奖中", vm.Lottery.LotteryPhaseText);
            var draw = new EntryDraw("draw", 0, lottery.Awards[0], lottery.Entries[0], DateTimeOffset.UnixEpoch, []);
            lottery = lottery with { Draws = [draw], DrawUntil = DateTimeOffset.UnixEpoch.AddSeconds(7) };
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery)));
            Assert.False(entry.FindControl<Button>("EndEntryButton")!.IsEnabled);
            Assert.False(GuiHarness.FindRowControl<Button>(entry, row, "DrawButton").IsEnabled);
            Assert.Contains("开奖中", panel.FindControl<TextBlock>("PhaseText")!.Text);
            var winners = entry.FindControl<ItemsControl>("WinnerGroups")!;
            GuiHarness.Flush();
            Assert.Contains(winners.GetRealizedContainers().SelectMany(row => row.GetVisualDescendants()).OfType<Border>(), chip => chip.Classes.Contains("fresh"));
            Assert.Contains(vm.Lottery.LotteryEntries, row => row.Award == "中 一等奖");
            lottery = lottery with { Phase = EntryLotteryPhase.Ended, DrawUntil = null };
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery)));
            Assert.True(panel.FindControl<Button>("NewButton")!.IsEnabled);
            Assert.Equal("抽完了", row.DrawText);
            Assert.Equal("收起画面", entry.FindControl<Button>("VisibilityButton")!.Content);
            GuiHarness.Flush();
            Assert.DoesNotContain(winners.GetRealizedContainers().SelectMany(row => row.GetVisualDescendants()).OfType<Border>(), chip => chip.Classes.Contains("fresh"));
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery with { Hidden = true })));
            Assert.Equal("重新显示", entry.FindControl<Button>("VisibilityButton")!.Content);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Awards_limit_default_grades_and_deferred_row_validation() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery() with { Awards = [TestLottery().Awards[0]] })));
            var entry = panel.FindControl<EntryLotteryPanel>("EntryContent")!;
            var rows = vm.Lottery.Awards;
            var add = entry.FindControl<Button>("AddAwardButton")!;
            var start = entry.FindControl<Button>("StartEntryButton")!;
            for (var count = 1; count < 5; count++)
            {
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                GuiHarness.Flush();
                var row = rows[count];
                Assert.Equal(count == 1 ? Grade.Second : Grade.Third, row.Grade);
                Assert.False(GuiHarness.FindRowControl<TextBlock>(entry, row, "ErrorMessage").IsVisible);
                Assert.False(start.IsEnabled);
                entry.FindControl<TextBox>("KeywordInput")!.Focus();
                Assert.True(GuiHarness.FindRowControl<TextBlock>(entry, row, "ErrorMessage").IsVisible);
                GuiHarness.FindRowControl<TextBox>(entry, row, "NameInput").Text = $"奖项{count + 1}";
                GuiHarness.FindRowControl<TextBox>(entry, row, "PrizeInput").Text = "贴纸";
                GuiHarness.Flush();
            }
            Assert.False(add.IsEnabled);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(5, rows.Count);
            var first = rows[0];
            var capacity = GuiHarness.FindRowControl<TextBox>(entry, first, "CapacityInput");
            foreach (var invalid in new[] { "", "0", "-1", "1.5", "2147483648", "abc" })
            {
                capacity.Text = invalid;
                GuiHarness.Flush();
                Assert.False(start.IsEnabled);
                Assert.Contains("invalid", capacity.Classes);
            }
            capacity.Text = "2";
            GuiHarness.Flush();
            Assert.True(start.IsEnabled);
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(
                GuiHarness.FindRowControl<Button>(entry, first, "RemoveButton"))).Invoke();
            Assert.Equal(4, rows.Count);
            Assert.True(add.IsEnabled);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task New_picker_and_inline_end_confirmation_allow_cancel_and_explicit_choice() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, LotteryPanelTests.TestLottery())));
            panel.FindControl<Button>("NewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var opened = Assert.IsType<NewLotteryWindow>(Assert.Single(window.OwnedWindows));
            opened.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(window.OwnedWindows);
            foreach (var mode in new[] { LotteryMode.Entry, LotteryMode.Pool })
            {
                var picker = new NewLotteryWindow();
                var result = picker.ShowDialog<LotteryMode?>(window);
                picker.FindControl<RadioButton>(mode == LotteryMode.Entry ? "EntryMode" : "PoolMode")!.IsChecked = true;
                picker.FindControl<Button>("NextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(mode, await result);
            }
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, TestLottery(EntryLotteryPhase.Closed))));
            var entry = panel.FindControl<EntryLotteryPanel>("EntryContent")!;
            var end = entry.FindControl<Button>("EndEntryButton")!;
            var confirm = entry.FindControl<Button>("ConfirmEndButton")!;
            end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(end.IsVisible);
            Assert.True(confirm.IsVisible);
            Assert.True(entry.FindControl<TextBlock>("ConfirmEndText")!.IsVisible);
            entry.FindControl<Button>("CancelEndButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(end.IsVisible);
            Assert.False(confirm.IsVisible);
            Assert.Equal(EntryLotteryPhase.Closed, Assert.IsType<EntryLottery>(vm.Lottery.LotteryState.Lottery).Phase);
            end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // The harness is not connected, so reaching EndEntry shows its error instead of ending.
            var error = entry.FindControl<TextBlock>("ErrorMessage")!;
            await GuiHarness.WaitForAsync(() => error.IsVisible);
            Assert.Equal("请先连接直播间。", error.Text);
            Assert.True(end.IsVisible);
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Draw_button_runs_the_entry_draw_for_its_award() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 976, Height = 720 };
        window.Show();
        try
        {
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true,
                TestLottery(EntryLotteryPhase.Closed) with { Entries = [new(42, "小明", DateTimeOffset.UnixEpoch)] })));
            var entry = panel.FindControl<EntryLotteryPanel>("EntryContent")!;
            GuiHarness.Flush();
            var draw = GuiHarness.FindRowControl<Button>(entry, vm.Lottery.Awards[1], "DrawButton");
            Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(draw)).Invoke();
            // The harness is not connected, so reaching DrawEntry shows its error instead of drawing.
            var error = entry.FindControl<TextBlock>("ErrorMessage")!;
            await GuiHarness.WaitForAsync(() => error.IsVisible);
            Assert.Equal("请先连接直播间。", error.Text);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
}
