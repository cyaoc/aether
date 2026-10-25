using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Aether.Gui.Tests;

public sealed class WelcomeOnlineRankTests
{
    [Fact]
    public Task Choosing_for_a_new_row_reveals_duplicates_and_preserves_other_rows() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "welcome:\n  uids: [42]\n");
        h.OnlineRankResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":12,"item":[{"uid":42,"name":"柚子"},{"uid":99,"name":"小鹿"}]}}"""));
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        var page = new WelcomeSettingsPage(h.DataDirectory) { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var original = Rows(page).Single(row => row.Kind == WelcomeCondition.Uid);
            var row = Add(page, WelcomeCondition.Uid);
            var picker = GuiHarness.FindRowControl<OnlineRankPicker>(page, row, "OnlinePicker");
            var button = picker.FindControl<Button>("PickerButton")!;
            Assert.Equal("从在线榜选第 2 位指定观众", new ButtonAutomationPeer(button).GetName());
            await Choose(0);
            Assert.Equal("42", GuiHarness.FindRowControl<TextBox>(page, row, "UidInput").Text);
            Assert.Equal("和上面那条重复了", GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage").Text);
            Assert.Equal("42", original.Uid);
            await Choose(1);
            Assert.Equal("99", row.Uid);
            Assert.False(GuiHarness.FindRowControl<TextBlock>(page, row, "ErrorMessage").IsVisible);
            Assert.True(GuiHarness.FindVisual<Button>(page, "PART_SaveButton").IsEnabled);
            button.Flyout!.ShowAt(button);
            await GuiHarness.WaitForAsync(() => picker.FindControl<ListBox>("RankList")!.ItemCount == 2);
            Assert.Equal(1, picker.FindControl<ListBox>("RankList")!.SelectedIndex);
            button.Flyout.Hide();
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([42L, 99L], Settings.Load(h.DataDirectory).WelcomeUids.Value);

            async Task Choose(int index)
            {
                button.Flyout!.ShowAt(button);
                await GuiHarness.WaitForAsync(() => picker.FindControl<ListBox>("RankList")!.ItemCount == 2);
                var list = picker.FindControl<ListBox>("RankList")!;
                list.SelectedIndex = index;
                list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                GuiHarness.Flush();
                Assert.False(button.Flyout.IsOpen);
            }
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Connection_and_read_only_state_control_each_welcome_picker(bool readOnly) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), readOnly
            ? "welcome: {enabled: true, texts: ['欢迎{昵称}'], uids: [42], fan_club_level: 21, guard: 提督, wealth_level: 45}\n"
            : "welcome:\n  uids: [42]\n");
        var vm = h.CreateViewModel();
        var page = new WelcomeSettingsPage(h.DataDirectory) { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var row = Rows(page).Single(row => row.Kind == WelcomeCondition.Uid);
            var picker = GuiHarness.FindRowControl<OnlineRankPicker>(page, row, "OnlinePicker");
            var button = picker.FindControl<Button>("PickerButton")!;
            var input = GuiHarness.FindRowControl<TextBox>(page, row, "UidInput");
            Assert.Equal(input.Bounds.Height, button.Bounds.Height);
            Assert.Equal(button.Bounds.Height, button.Bounds.Width);
            Assert.False(button.IsEnabled);
            Assert.Equal("连上直播间后才能选", ToolTip.GetTip(button));
            vm.Apply(new Connected(7734200, 24680));
            Assert.Equal(!readOnly, button.IsEnabled);
            Assert.Equal(readOnly ? "此项只读，不能从在线榜选" : "从在线榜选", ToolTip.GetTip(button));
            vm.Apply(new Reconnecting());
            Assert.False(button.IsEnabled);
            Assert.Equal("连上直播间后才能选", ToolTip.GetTip(button));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

}
