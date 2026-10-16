using Aether.Core;
using Aether.Gui.Tests.Support;
using static Aether.Gui.Tests.Support.ConditionList;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Aether.Gui.Tests;

public sealed class VisualReviewTests
{
    [Theory]
    [InlineData(904, false)]
    [InlineData(904, true)]
    [InlineData(960, false)]
    [InlineData(960, true)]
    public Task Welcome_large_lists_keep_conditions_in_the_first_screen(int width, bool dark) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            File.WriteAllText(Settings.FilePath(harness.DataDirectory),
                $"welcome:\n  texts: {Settings.TextListText(Enumerable.Range(1, 12).Select(i => $"欢迎{{昵称}}回到直播间 · {i}"))}\n  uids: [{string.Join(", ", Enumerable.Range(12345670, 20))}]\n  fan_club_level: 21\n  guard: 舰长\n  wealth_level: 45\n");
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = width, Height = 640,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 2;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(p => p.IsEffectivelyVisible);
                var heading = page.FindControl<TextBlock>("ConditionsHeading")!;
                var scroll = heading.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.Equal(0, scroll.Offset.Y);
                Assert.InRange(heading.TranslatePoint(default, scroll)!.Value.Y, 0, scroll.Bounds.Height - heading.Bounds.Height);
                Assert.Equal(3, WelcomeTextList.Rows(page).Count(row => row.IsVisible));
                Assert.Equal(3, Rows(page).Count(row => row.Kind == WelcomeCondition.Uid && row.IsVisible));
                foreach (var row in Rows(page).Where(row => row.Kind == WelcomeCondition.Uid && row.IsVisible))
                {
                    var input = row.FindControl<TextBox>("UidInput")!;
                    var remove = row.FindControl<Button>("RemoveButton")!;
                    Assert.True(input.TranslatePoint(default, row)!.Value.X + input.Bounds.Width <= remove.TranslatePoint(default, row)!.Value.X);
                }
                Capture("collapsed");
                page.FindControl<Button>("UidFold")!.BringIntoView();
                GuiHarness.Flush();
                Capture("uids");
                ShowMenu(page);
                Capture("menu");
                Menu(page).Hide();
                return true;
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }

            void Capture(string state)
            {
                if (HeadlessApp.ScreenshotDirectory is not null)
                    Save(window, $"welcome-lists-{state}-{width}-{(dark ? "dark" : "light")}");
            }
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("condition", false)]
    [InlineData("condition", true)]
    [InlineData("animation", false)]
    [InlineData("animation", true)]
    public Task Screenshots_list_row_validation_states(string kind, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(() =>
        {
            using var harness = new GuiHarness();
            Directory.CreateDirectory(harness.DataDirectory);
            var video = Path.Combine(harness.DataDirectory, "欢迎动画.mp4");
            File.WriteAllText(video, "video");
            var page = new SettingsPage(harness.DataDirectory,
                kind == "animation" ? SettingsSection.WelcomeAnimation : SettingsSection.Welcome,
                () => Task.FromResult<string?>(video));
            var window = new Window
            {
                Content = page, Width = 736, Height = 640,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Control row;
                if (kind == "condition") row = Add(page, WelcomeCondition.Uid);
                else
                {
                    page.FindControl<Button>(kind == "text" ? "AddWelcomeTextButton" : "AddAnimationButton")!
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    GuiHarness.Flush();
                    row = kind == "text" ? WelcomeTextList.Rows(page)[1]
                        : page.FindControl<StackPanel>("AnimationRows")!.Children.Single();
                }
                var input = row.FindControl<TextBox>(kind == "text" ? "ValueInput" : "UidInput")!;
                Capture("new", false);
                page.FindControl<Button>(kind == "animation" ? "AddAnimationButton" : "AddWelcomeTextButton")!.Focus();
                Capture("empty-blurred", true);
                input.Focus();
                input.Text = kind == "text" ? "{@}" : "0";
                Capture("invalid-editing", true);
                input.Text = kind == "text" ? "欢迎回来{昵称}" : "12345";
                Capture("corrected", false);

                void Capture(string state, bool invalid)
                {
                    row.BringIntoView();
                    GuiHarness.Flush();
                    var border = input.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
                    var color = Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color;
                    Assert.Equal(invalid, color == Color.Parse(dark ? "#F87171" : "#B91C1C"));
                    Save(window, $"list-row-{kind}-{state}-{(dark ? "dark" : "light")}");
                }
            }
            finally { window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(960, false)]
    [InlineData(960, true)]
    [InlineData(904, false)]
    [InlineData(904, true)]
    public Task Screenshots_welcome_text_lists(int width, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            Settings.Save(harness.DataDirectory, new Dictionary<string, string>
            {
                ["welcome.enabled"] = "true", ["welcome.fan_club_level"] = "12",
                ["welcome.texts"] = Settings.TextListText(["欢迎{大航海}{昵称}", "{@}欢迎回家", "晚上好{昵称}，今天也来啦"])
            });
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = width, Height = 640,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 2;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(p => p.Section == SettingsSection.Welcome);
                Capture("three");
                page.FindControl<Button>("AddWelcomeTextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture("empty-row");
                WelcomeTextList.Input(page, 3).Text = "欢迎回到直播间";
                Capture("four");
                foreach (var row in WelcomeTextList.Rows(page).Skip(1))
                    row.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Capture("one");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(string state) => Save(window, $"welcome-texts-{state}-{width}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(960, 640, false)]
    [InlineData(960, 640, true)]
    [InlineData(760, 520, false)]
    [InlineData(760, 520, true)]
    public Task Screenshots_welcome_animation_states(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var videos = new Dictionary<long, string> { [1234567890123456] = "小鹿的欢迎动画.mp4", [67890] = "星光入场.webm", [24680] = "很长的专属欢迎动画文件名.mp4", [13579] = "找不到的视频.mp4" };
            Directory.CreateDirectory(Path.Combine(harness.DataDirectory, "welcome-animations"));
            foreach (var (uid, file) in videos)
            {
                Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, uid, file);
                if (uid != 13579) File.WriteAllText(Path.Combine(harness.DataDirectory, "welcome-animations", file), "video");
            }
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["welcome_animation.enabled"] = "true" });
            var viewModel = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = width, Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 3;
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.Section == SettingsSection.WelcomeAnimation);
                viewModel.Apply(new AnimationPageStatus(true, 17890, 2));
                foreach (var (state, index) in new[] { WelcomeAnimationState.Completed, WelcomeAnimationState.Failed, WelcomeAnimationState.NoReport, WelcomeAnimationState.Skipped, WelcomeAnimationState.Queued }.Select((state, index) => (state, index)))
                    viewModel.Apply(new WelcomeAnimationPlayback(index.ToString(), index == 1 ? 67890 : 24680, index == 1 ? "星光入场.webm" : "很长的专属欢迎动画文件名.mp4", index == 0, state, state == WelcomeAnimationState.Skipped ? "10 分钟内播过" : null,
                        Nickname: index switch { 1 => "柚子不酸", 2 => "bili_6508617762", _ => null }));
                Capture("connected");
                viewModel.Apply(new AnimationPageStatus(true, 17890, 0));
                Capture("setup");
                viewModel.Apply(new AnimationPageStatus(false, 17890, 0, "端口 17890 被别的程序占用"));
                Capture("port-occupied");
                viewModel.Apply(new AnimationPageStatus(true, 17890, 1));
                viewModel.Apply(new WelcomeAnimationPlayback("playing", 1234567890123456, videos[1234567890123456], true, WelcomeAnimationState.Playing));
                Capture("playing");
                var rows = page.FindControl<StackPanel>("AnimationRows")!.Children.Cast<WelcomeAnimationRow>().ToArray();
                rows[0].FindControl<TextBox>("UidInput")!.Text = "";
                rows[1].FindControl<TextBox>("UidInput")!.Text = "not-a-uid";
                rows[2].FindControl<TextBox>("UidInput")!.Text = "13579";
                Capture("row-errors");
                // Reload after deleting the fixture mappings to show the first-use state.
                foreach (var row in rows) row.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                page.FindControl<ToggleSwitch>("AnimationInput")!.IsChecked = false;
                page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await GuiHarness.WaitForAsync(() => page.IsEnabled);
                viewModel.RecentAnimations.Clear();
                page.FindControl<TextBlock>("Message")!.Text = "";
                viewModel.Apply(new AnimationPageStatus(false, 17890, 0));
                Capture("empty");
                page.FindControl<Control>("EmptyAnimations")!.BringIntoView();
                Capture("empty-list");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void Capture(string state) => Save(window, $"animation-{state}-{width}x{height}-{(dark ? "dark" : "light")}");
        }, TestContext.Current.CancellationToken);
    }

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
                foreach (var (pageIndex, name) in new[] { (5, "LogLevelInput"), (2, "GuardInput") })
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
                window.Navigation.SelectedIndex = 4;
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
                window.Navigation.SelectedIndex = 5;
                window.Navigation.SelectedIndex = 4;
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
            // Pure-English nicknames are the ones whose baselines drifted (#124); the last one has no break opportunity.
            var queueNames = new[] { names[0], "bili_6508617762", names[2], names[3], "bili_6508617762", GuiHarness.LongEnglishNickname };
            for (var i = 0; i < queueNames.Length; i++)
            {
                var nickname = queueNames[i];
                viewModel.Apply(new SendQueueUpdate(i + 1, time.AddSeconds(-i * 12), SendQueueItemKind.Welcome,
                    i == 1 ? SendQueueItemState.Queued : i == 3 ? SendQueueItemState.Discarded : SendQueueItemState.Sent, 1000 + i, nickname)
                { Text = "欢迎" + nickname, WelcomeCondition = i % 2 == 0 ? WelcomeCondition.Guard : WelcomeCondition.FanClubLevel,
                    WelcomeValue = i % 2 == 0 ? "舰长" : "12", Reason = i == 3 ? "进场超过 30 秒" : null });
                viewModel.Apply(new SendQueueUpdate(i + 10, time.AddSeconds(-i * 12), i % 2 == 0 ? SendQueueItemKind.BlindBoxQuery : SendQueueItemKind.BlindBoxAnnouncement,
                    i == 1 ? SendQueueItemState.WaitingToRetry : SendQueueItemState.Sent, 1000 + i, nickname)
                { Text = "@" + nickname + " 投入52电池 亏12.6电池", RetrySeconds = 1 });
            }
            var navigation = window.FindControl<ListBox>("Navigation")!;
            var pages = new[] { "danmaku", "blind-box", "welcome", "welcome-animation", "send-queue", "general" };
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
            WelcomeTextList.Input(welcome).Text = "欢迎{昵称}回到直播间";
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
            navigation.SelectedIndex = 5;
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
