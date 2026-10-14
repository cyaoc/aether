using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests;

public sealed class VisualReviewTests
{
    // A manual review tool, not a regression test: set AETHER_SCREENSHOT_DIR to render every page per size and theme
    // with Skia and compare the PNGs with designs B, E and F. CI leaves it unset, so the theory skips there.
    [Theory]
    [InlineData(960, 640, false)]
    [InlineData(960, 640, true)]
    [InlineData(760, 520, false)]
    [InlineData(760, 520, true)]
    public Task Screenshots_every_page_for_visual_review(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return Render(width, height, dark);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Screenshots_recent_room_input_and_dropdown(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            harness.SaveRecentRooms("1768506153", "6", "7734200", "123456", "42");
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                Capture(window, "normal");
                var input = window.FindControl<ComboBox>("RoomInput")!;
                input.GetVisualDescendants().OfType<TextBox>().Single().Focus();
                Capture(window, "focus");
                var arrow = input.TranslatePoint(new Point(input.Bounds.Width - 16, input.Bounds.Height / 2), window)!.Value;
                window.MouseDown(arrow, MouseButton.Left);
                window.MouseUp(arrow, MouseButton.Left);
                Capture(window, "open");
                Assert.True(input.IsDropDownOpen);
                Capture(TopLevel.GetTopLevel(input.ContainerFromIndex(0)!)!, "list");
                var remove = input.ContainerFromIndex(1)!.GetVisualDescendants().OfType<Button>().Single();
                var popup = TopLevel.GetTopLevel(remove)!;
                popup.MouseMove(remove.TranslatePoint(new Point(12, 12), popup)!.Value);
                Capture(popup, "delete-hover");
                foreach (var room in viewModel.RecentRooms.ToArray()) viewModel.RemoveRecentRoomCommand.Execute(room);
                Capture(window, "empty");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(TopLevel topLevel, string state) => Save(topLevel, $"room-{state}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Screenshots_settings_dropdowns_and_condition_menu(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                foreach (var (pageIndex, name) in new[] { (4, "LogLevelInput"), (2, "GuardInput") })
                {
                    var input = GuiHarness.VisibleComboBox(window, pageIndex, name);
                    input.IsDropDownOpen = true;
                    GuiHarness.Flush();
                    Capture(TopLevel.GetTopLevel(input.ContainerFromIndex(0)!)!, name);
                    input.IsDropDownOpen = false;
                }
                var menu = ShowMenu(window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible));
                var item = menu.Items.Cast<MenuItem>().First(x => x.IsEnabled);
                var popup = TopLevel.GetTopLevel(item)!;
                popup.MouseMove(item.TranslatePoint(new Point(10, 10), popup)!.Value);
                Capture(popup, "condition-menu");
                menu.Hide();
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(TopLevel topLevel, string name) => Save(topLevel, $"{name}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Screenshots_numeric_input_states_with_real_font_metrics(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.Navigation.SelectedIndex = 3;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                var number = page.FindControl<NumericUpDown>("SendIntervalInput")!;
                var text = number.GetVisualDescendants().OfType<TextBox>().Single();
                Capture("minimum");
                text.Text = "12345";
                Capture("normal");
                var increase = number.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PART_IncreaseButton");
                window.MouseMove(increase.TranslatePoint(new Point(10, 10), window)!.Value);
                Capture("hover");
                window.MouseMove(default);
                text.Focus();
                Capture("focus");
                var viewer = text.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width, $"Five digits need {viewer.Extent.Width}, available {viewer.Viewport.Width}");
                text.Text = "1.5";
                Capture("error");
                text.Text = "1";
                File.WriteAllText(Settings.FilePath(harness.DataDirectory), "send:\n  interval_seconds: |-\n    1\n");
                window.Navigation.SelectedIndex = 4;
                window.Navigation.SelectedIndex = 3;
                Capture("readonly");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(string state) => Save(window, $"numeric-{state}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }

    private static Task Render(int width, int height, bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        harness.NavResponse = _ => Task.FromResult(GuiHarness.AccountResponse("Aether助手"));
        var roomStarted = new TaskCompletionSource();
        harness.RoomResponse = async (_, token) =>
        {
            roomStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        Settings.Load(harness.DataDirectory);
        var path = Settings.FilePath(harness.DataDirectory);
        File.WriteAllText(path, File.ReadAllText(path).Replace("enabled: false", "enabled: true").Replace("uids: []", "uids: [1234567890123456, 67890]"));
        var clock = new FakeTimeProvider();
        var viewModel = harness.CreateViewModel(clock);
        viewModel.RoomNumber = "1768506153";
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            viewModel.ConnectCommand.Execute(null);
            await roomStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Apply(new Connected());
            clock.Advance(TimeSpan.FromMinutes(42));
            var time = new DateTimeOffset(2026, 10, 12, 20, 41, 0, TimeSpan.FromHours(8));
            var names = new[] { "一只小鹿", "柚子不酸", "Aether助手", "晚风吹过山岗", "夜航船", "是阿橙呀", "momo_7", "路过的猫" };
            var messages = new[] { "来了来了", "今日盲盒", "投入52电池 亏12.6电池", "哈哈哈哈哈哈这个反应", "主播今天状态好好", "刚下班，今天播到几点？", "这首歌叫什么名字", "晚上好！这是一条用来检查自动换行的较长弹幕，窄窗口也应该能完整阅读。" };
            var tiers = new[] { 0, 1, 1, 2, 3, 0, 1, 0 };
            long?[] fanClubs = [null, 21, 47, 33, 40, null, 12, null];
            var wealth = new[] { 12, 30, 45, 41, 52, 8, 18, 0 };
            for (var i = 0; i < names.Length; i++)
                viewModel.Apply(new Danmaku(time.AddSeconds(i * 3), names[i], messages[i])
                {
                    Uid = names[i] == "Aether助手" ? 123 : 1000 + i,
                    GuardTier = tiers[i], FanClubLevel = fanClubs[i], WealthLevel = wealth[i],
                    ReplyName = i == 2 ? "柚子不酸" : i == 6 ? "夜航船" : ""
                });
            for (var i = 0; i < 6; i++)
            {
                viewModel.Apply(new SendQueueUpdate(i + 1, time.AddSeconds(-i * 12), SendQueueItemKind.Welcome,
                    i == 1 ? SendQueueItemState.Queued : i == 3 ? SendQueueItemState.Discarded : SendQueueItemState.Sent, 1000 + i, names[i])
                { Text = "欢迎" + names[i], WelcomeCondition = i % 2 == 0 ? WelcomeCondition.Guard : WelcomeCondition.FanClubLevel,
                    WelcomeValue = i % 2 == 0 ? "舰长" : "12", Reason = i == 3 ? "进场超过 30 秒" : null });
                viewModel.Apply(new SendQueueUpdate(i + 10, time.AddSeconds(-i * 12), i % 2 == 0 ? SendQueueItemKind.BlindBoxQuery : SendQueueItemKind.BlindBoxAnnouncement,
                    i == 1 ? SendQueueItemState.WaitingToRetry : SendQueueItemState.Sent, 1000 + i, names[i])
                { Text = "@" + names[i] + " 投入52电池 亏12.6电池", RetrySeconds = 1 });
            }
            var navigation = window.FindControl<ListBox>("Navigation")!;
            var pages = new[] { "danmaku", "blind-box", "welcome", "send-queue", "general" };
            for (var i = 0; i < pages.Length; i++)
            {
                navigation.SelectedIndex = i;
                Capture(pages[i]);
                if (i == 2)
                {
                    var uid = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.IsEffectivelyVisible && x.Text == "1234567890123456");
                    var viewer = uid.GetVisualDescendants().OfType<ScrollViewer>().Single();
                    Assert.True(viewer.Extent.Width <= viewer.Viewport.Width);
                }
            }
            navigation.SelectedIndex = 2;
            GuiHarness.Flush();
            var welcome = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
            welcome.FindControl<TextBox>("WelcomeTextInput")!.Text = "欢迎{昵称}回到直播间";
            GuiHarness.Flush();
            welcome.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture("welcome-reconnect");
            var add = welcome.FindControl<Button>("AddConditionButton")!;
            var menu = Menu(welcome);
            add.BringIntoView();
            GuiHarness.Flush();
            menu.ShowAt(add);
            Capture("welcome-menu");
            menu.Hide();
            foreach (var row in Rows(welcome)) Remove(row);
            Capture("welcome-empty");
            add.BringIntoView();
            GuiHarness.Flush();
            menu.ShowAt(add);
            Capture("welcome-empty-menu");
            menu.Hide();
            Add(welcome, WelcomeCondition.Uid);
            Capture("welcome-invalid");
            Remove(Rows(welcome).Single());
            foreach (var kind in new[] { WelcomeCondition.FanClubLevel, WelcomeCondition.Guard, WelcomeCondition.WealthLevel })
                Add(welcome, kind);
            Capture("welcome-levels");
            File.WriteAllText(path, "log:\n  level: @bad\n");
            navigation.SelectedIndex = 4;
            Capture("settings-error");
            viewModel.IsLoginPage = true;
            viewModel.Apply(new WatchQrCode("https://example.test/visual-review"));
            Capture("login");
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;

        void Capture(string page) => Save(window, $"{page}-{width}x{height}-{(dark ? "dark" : "light")}");
    }, TestContext.Current.CancellationToken);

    private static void Save(TopLevel topLevel, string fileName)
    {
        GuiHarness.Flush();
        using var bitmap = topLevel.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        Directory.CreateDirectory(HeadlessApp.ScreenshotDirectory!);
        bitmap.Save(Path.Combine(HeadlessApp.ScreenshotDirectory!, fileName + ".png"), PngBitmapEncoderOptions.Default);
    }
}
