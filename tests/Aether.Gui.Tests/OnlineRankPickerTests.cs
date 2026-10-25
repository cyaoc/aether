using System.Collections.Immutable;
using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Input.Raw;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Aether.Gui.Tests;

public sealed class OnlineRankPickerTests
{
    [Fact]
    public Task Choosing_a_viewer_fills_uid_and_shows_nickname_until_edited() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        h.OnlineRankResponse = (uri, _) =>
        {
            Assert.Contains("ruid=24680&room_id=7734200", uri.Query);
            return Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":12,"item":[{"uid":42,"name":"柚子"},{"uid":99,"name":"小鹿"}]}}"""));
        };
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            button.Flyout!.ShowAt(button);
            await GuiHarness.WaitForAsync(() => picker.FindControl<ListBox>("RankList")!.ItemCount == 2);
            var list = picker.FindControl<ListBox>("RankList")!;
            list.SelectedIndex = 0;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("42", page.FindControl<TextBox>("UidInput")!.Text);
            Assert.False(button.Flyout!.IsOpen);
            Assert.Equal("柚子 · 在线榜", page.FindControl<TextBlock>("ViewerHint")!.Text);
            page.FindControl<TextBox>("UidInput")!.Text = "99";
            page.FindControl<TextBox>("UidInput")!.Text = "42";
            Assert.Equal("这场还没发过弹幕，喊停后显示昵称", page.FindControl<TextBlock>("ViewerHint")!.Text);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Mouse_and_keyboard_choose_with_existing_uid_highlighted(bool keyboard) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        h.OnlineRankResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":12,"item":[{"uid":42,"name":"柚子"},{"uid":99,"name":"小鹿"}]}}"""));
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        vm.Lottery.Uid = "42";
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            Assert.Equal("从在线榜选喊停观众", new ButtonAutomationPeer(button).GetName());
            GuiHarness.Flush();
            if (keyboard)
            {
                page.FindControl<TextBox>("UidInput")!.Focus();
                window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Assert.True(button.IsFocused);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            }
            else
            {
                var point = button.TranslatePoint(new Point(16, 16), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            await GuiHarness.WaitForAsync(() => picker.FindControl<ListBox>("RankList")!.ItemCount == 2);
            GuiHarness.Flush();
            var list = picker.FindControl<ListBox>("RankList")!;
            Assert.Equal(0, list.SelectedIndex);
            Assert.Equal("前 2 位 · 在线 12 人", picker.FindControl<TextBlock>("RankSummary")!.Text);
            var popup = TopLevel.GetTopLevel(list)!;
            if (keyboard)
            {
                Assert.True(list.IsKeyboardFocusWithin, $"Focused: {popup.FocusManager?.GetFocusedElement()}");
                popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.Equal(1, list.SelectedIndex);
                popup.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                Assert.Equal(1, list.SelectedIndex);
                popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            }
            else
            {
                var item = list.ContainerFromIndex(1)!;
                var point = item.TranslatePoint(new Point(10, 10), popup)!.Value;
                popup.MouseDown(point, MouseButton.Left);
                popup.MouseUp(point, MouseButton.Left);
            }
            Assert.Equal("99", page.FindControl<TextBox>("UidInput")!.Text);
            Assert.False(button.Flyout!.IsOpen);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Connection_and_read_only_state_disable_the_button_with_a_reason() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            Assert.False(button.IsEnabled);
            Assert.Equal("连上直播间后才能选", ToolTip.GetTip(button));
            Assert.True(ToolTip.GetShowOnDisabled(button));
            vm.Apply(new Connected(7734200, 24680));
            Assert.True(button.IsEnabled);
            Assert.Equal("从在线榜选", ToolTip.GetTip(button));
            vm.Apply(new Reconnecting());
            Assert.False(button.IsEnabled);
            Assert.Equal("连上直播间后才能选", ToolTip.GetTip(button));
            vm.Apply(new Connected(7734200, 24680));
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool() with { Phase = PoolLotteryPhase.Spinning })));
            Assert.False(button.IsEnabled);
            Assert.Equal("转动中不能换人", ToolTip.GetTip(button));
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);


    [Fact]
    public Task Reopening_reads_again_and_cancels_loading_on_escape_and_disconnect() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var requested = new TaskCompletionSource<CancellationToken>();
        h.OnlineRankResponse = async (_, token) =>
        {
            requested.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return GuiHarness.Json("{}");
        };
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            button.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            var token = await requested.Task;
            GuiHarness.Flush();
            Assert.True(picker.FindControl<Grid>("Loading")!.IsVisible);
            var popup = TopLevel.GetTopLevel(picker.FindControl<Grid>("Loading")!)!;
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            popup.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            GuiHarness.Flush();
            Assert.False(button.Flyout!.IsOpen);
            Assert.True(token.IsCancellationRequested);
            requested = new();
            button.Flyout.ShowAt(button);
            token = await requested.Task;
            vm.ConnectionState = RoomConnectionState.Disconnected;
            Assert.True(token.IsCancellationRequested);
            Assert.False(button.Flyout!.IsOpen);
            h.OnlineRankResponse = (uri, _) =>
            {
                Assert.Contains("ruid=555&room_id=999", uri.Query);
                return Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":0,"item":[]}}"""));
            };
            vm.Apply(new Connected(999, 555));
            button.Flyout.ShowAt(button);
            await GuiHarness.WaitForAsync(() => picker.FindControl<TextBlock>("MessageText")!.Text == "在线榜上还没有人");
            Assert.Equal("", picker.FindControl<TextBlock>("RankSummary")!.Text);
            Assert.Equal("没开播时一般是空的；也可以直接填 UID", picker.FindControl<TextBlock>("MessageHint")!.Text);
            button.Flyout.Hide();
            h.OnlineRankResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":-400,"message":"请求错误"}"""));
            button.Flyout.ShowAt(button);
            await GuiHarness.WaitForAsync(() => picker.FindControl<TextBlock>("MessageText")!.Text!.Contains("请求错误"));
            Assert.StartsWith("读不到在线榜：读取在线榜", picker.FindControl<TextBlock>("MessageText")!.Text);
            Assert.Equal("关掉再点一次会重新读；也可以直接填 UID", picker.FindControl<TextBlock>("MessageHint")!.Text);
            button.Flyout.Hide();
            page.FindControl<TextBox>("UidInput")!.Text = "123";
            Assert.Equal("123", vm.Lottery.Uid);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Late_response_after_read_only_cannot_repopulate_reopened_picker() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>();
        h.OnlineRankResponse = (_, _) => oldResponse.Task;
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            button.Flyout!.ShowAt(button);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool() with { Phase = PoolLotteryPhase.Spinning })));
            Assert.False(button.Flyout!.IsOpen);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
            h.OnlineRankResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":0,"item":[]}}"""));
            button.Flyout.ShowAt(button);
            oldResponse.SetResult(GuiHarness.Json("""{"code":0,"data":{"count":1,"item":[{"uid":42,"name":"过期"}]}}"""));
            await GuiHarness.WaitForAsync(() => picker.FindControl<TextBlock>("MessageText")!.Text == "在线榜上还没有人");
            Assert.Equal(0, picker.FindControl<ListBox>("RankList")!.ItemCount);
            Assert.Equal("", vm.Lottery.Uid);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Danmaku_name_takes_priority_and_new_lottery_forgets_rank_name() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        h.OnlineRankResponse = (_, _) => Task.FromResult(GuiHarness.Json("""{"code":0,"data":{"count":1,"item":[{"uid":42,"name":"在线昵称"}]}}"""));
        var vm = h.CreateViewModel();
        vm.Apply(new Connected(7734200, 24680));
        vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool())));
        var page = new LotteryPanel { DataContext = vm };
        var window = new Window { Content = page, Width = 976, Height = 720 };
        window.Show();
        try
        {
            var picker = page.FindControl<OnlineRankPicker>("OnlinePicker")!;
            var button = picker.FindControl<Button>("PickerButton")!;
            button.Flyout!.ShowAt(button);
            await GuiHarness.WaitForAsync(() => picker.FindControl<ListBox>("RankList")!.ItemCount == 1);
            var list = picker.FindControl<ListBox>("RankList")!;
            list.SelectedIndex = 0;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("在线昵称 · 在线榜", page.FindControl<TextBlock>("ViewerHint")!.Text);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool() with { Viewers = ImmutableDictionary<long, string>.Empty.Add(42, "弹幕昵称") })));
            Assert.Equal("弹幕昵称 · 这场发过弹幕", page.FindControl<TextBlock>("ViewerHint")!.Text);
            vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, Pool() with { Id = "new", Uid = 42 })));
            Assert.Equal("这场还没发过弹幕，喊停后显示昵称", page.FindControl<TextBlock>("ViewerHint")!.Text);
        }
        finally { window.Close(); await vm.ShutdownAsync(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static PoolLottery Pool() => new("pool", 7734200, "停", DateTimeOffset.UtcNow,
        [new("抱枕", 1, Grade.Grand)], [], ImmutableDictionary<long, string>.Empty);
}
