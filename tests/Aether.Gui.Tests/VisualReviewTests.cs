using System.Runtime.InteropServices;
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
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_lottery_history(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var h = new GuiHarness();
            await LotteryHistoryFixture.SeedAsync(h);
            var vm = h.CreateViewModel();
            var window = new MainWindow(h.DataDirectory)
                { DataContext = vm, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                await vm.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 4;
                vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, LotteryHistoryFixture.Current)));
                var panel = window.GetVisualDescendants().OfType<LotteryPanel>().Single();
                var tabs = panel.FindControl<ListBox>("ViewTabs")!;
                tabs.SelectedIndex = 1;
                await vm.Lottery.RefreshLotteryHistoryAsync();
                // Compare the selected tab after its theme transition settles.
                GuiHarness.Flush();
                await Task.Delay(300, TestContext.Current.CancellationToken);
                foreach (var visual in tabs.GetVisualDescendants()) visual.InvalidateVisual();
                Save(window, $"history-entry-{width}-{(dark ? "dark" : "light")}");
                await vm.Lottery.RefreshLotteryHistoryAsync("pool");
                Save(window, $"history-pool-{width}-{(dark ? "dark" : "light")}");
            }
            finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_entry_lottery_and_picker(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var h = new GuiHarness();
            await h.SaveCredentialAsync();
            var vm = h.CreateViewModel();
            var window = new MainWindow(h.DataDirectory)
                { DataContext = vm, Width = width, Height = height, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                await vm.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 4;
                var at = new DateTimeOffset(2026, 10, 17, 20, 30, 0, TimeSpan.FromHours(8));
                string[] names = ["一只小鹿", "柚子不酸", "夜航船", "桃桃乌龙", "番茄炒蛋", "青柠", "橘猫本猫", "南巷"];
                var lottery = EntryLotteryPanelTests.TestLottery() with
                {
                    Entries = [.. names.Select((name, index) => new LotteryEntry(index + 1, name, at.AddSeconds(index * 13)))],
                };
                foreach (var phase in Enum.GetValues<EntryLotteryPhase>())
                {
                    var draws = phase is EntryLotteryPhase.Closed or EntryLotteryPhase.Ended
                        ? Enumerable.Range(0, phase == EntryLotteryPhase.Ended ? 6 : 2).Select(index =>
                        {
                            var award = phase == EntryLotteryPhase.Closed ? 2 : index == 0 ? 0 : index < 3 ? 1 : 2;
                            return new EntryDraw($"d{index}", award, lottery.Awards[award], lottery.Entries[index], at, []);
                        }).ToArray() : [];
                    vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, lottery with
                    {
                        Phase = phase, Entries = phase == EntryLotteryPhase.Preparing ? [] : lottery.Entries,
                        Draws = [.. draws], DrawUntil = phase == EntryLotteryPhase.Closed ? at.AddSeconds(7) : null,
                    })));
                    Save(window, $"entry-{phase}-{width}-{(dark ? "dark" : "light")}");
                    if (phase != EntryLotteryPhase.Closed) continue;
                    vm.Lottery.Apply(new LotteryUpdate(vm.Lottery.LotteryState with { Lottery = vm.Lottery.LotteryState.Lottery! with { DrawUntil = null } }));
                    window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "EndEntryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Save(window, $"entry-Closed-confirm-{width}-{(dark ? "dark" : "light")}");
                }
                var picker = new NewLotteryWindow { RequestedThemeVariant = window.RequestedThemeVariant };
                var picked = picker.ShowDialog<LotteryMode?>(window);
                Save(picker, $"lottery-picker-{width}-{(dark ? "dark" : "light")}");
                picker.Close(null);
                await picked;
            }
            finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Screenshots_pool_lottery(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var vm = harness.CreateViewModel();
            var window = new MainWindow(harness.DataDirectory)
                { DataContext = vm, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            try
            {
                await vm.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 4;
                // Canvas V1's pool and records: a viewer's stop, a host stop with no UID, and a host stop for a named viewer.
                PoolPrize[] prizes = [new("机械键盘", 1, Grade.Grand), new("周边抱枕", 3, Grade.Second),
                    new("签名照", 4, Grade.Third), new("贴纸套装", 2, Grade.Third)];
                var at = new DateTimeOffset(2026, 10, 17, 20, 30, 0, TimeSpan.FromHours(8));
                var session = LotteryPanelTests.TestLottery() with
                {
                    Prizes = [.. prizes],
                    Draws =
                    [
                        new("d1", prizes[3], 42, "小明", false, at, []),
                        new("d2", prizes[1], null, null, true, at.AddMinutes(2), []),
                        new("d3", prizes[2], 42, "小明", true, at.AddMinutes(6), []),
                    ],
                };
                foreach (var phase in Enum.GetValues<PoolLotteryPhase>())
                {
                    vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, session with { Phase = phase })));
                    Save(window, $"lottery-{phase}-{(dark ? "dark" : "light")}");
                }
                vm.Lottery.Apply(new LotteryUpdate(new(7734200, true, session with { DrawUntil = at.AddMinutes(6).AddSeconds(7) })));
                Save(window, $"lottery-Drawing-{(dark ? "dark" : "light")}");
            }
            finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Main_window_has_desktop_default_and_minimum_size() => HeadlessApp.Session.Dispatch(() =>
    {
        using var harness = new GuiHarness();
        var window = new MainWindow(harness.DataDirectory);
        Assert.Equal(new Size(1200, 720), new Size(window.Width, window.Height));
        Assert.Equal(new Size(1040, 600), new Size(window.MinWidth, window.MinHeight));
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Fixed_text_stays_on_one_line_at_supported_sizes(int width, int height, bool dark) =>
        HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
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
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 0, 0));
                for (var index = 0; index < window.Navigation.ItemCount; index++)
                {
                    window.Navigation.SelectedIndex = index;
                    CheckFixedText();
                }
                var general = window.GetVisualDescendants().OfType<SettingsPage>().Single(p => p.IsEffectivelyVisible);
                var status = general.FindControl<TextBlock>("WebServiceStatusText")!;
                status.Text = string.Concat(Enumerable.Repeat("没打开：端口 17890 被别的程序占用。", 4));
                GuiHarness.Flush();
                Assert.True(Assert.Single(status.TextLayout.TextLines).HasCollapsed);
                Assert.Equal(status.Text, ToolTip.GetTip(status));
                // Headless drawing does not hit-test glyphs; exercise hover with the Skia screenshot run.
                if (HeadlessApp.ScreenshotDirectory is not null)
                {
                    ToolTip.SetShowDelay(status, 0);
                    var hoverPoint = status.TranslatePoint(new Point(3, 3), window)!.Value;
                    Assert.Same(status, window.InputHitTest(hoverPoint));
                    window.MouseMove(hoverPoint);
                    await GuiHarness.WaitForAsync(() => ToolTip.GetIsOpen(status));
                    window.MouseMove(default);
                }
                viewModel.IsLoginPage = true;
                viewModel.Apply(new WatchQrCode("https://example.test/visual-review"));
                CheckFixedText();
                viewModel.IsLoginPage = false;
                CheckFixedText();
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;

            void CheckFixedText()
            {
                GuiHarness.Flush();
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text));
                foreach (var text in texts)
                {
                    // These are multi-sentence help, not single-line captions.
                    if (text.Name is "WelcomeTextHint" or "WebServiceHelp" ||
                        text.GetVisualAncestors().OfType<Control>().Any(c =>
                            c.Name is "VideoSetupHelp" or "LotterySetupHelp" or "EmptyConditions")) continue;
                    Assert.True(text.TextWrapping == TextWrapping.NoWrap, $"Wrapping: {text.Text}");
                    var line = Assert.Single(text.TextLayout.TextLines);
                    Assert.True(line.Width > 0 && text.Bounds.Height >= line.Height - 1, $"Hidden: {text.Text}");
                    Assert.True(line.Width <= text.Bounds.Width + 1, $"Clipped: {text.Text}");
                    if (line.HasCollapsed) Assert.Equal(text.Text, ToolTip.GetTip(text));
                    if (text.Classes.Contains("section") && text.Parent is Panel row)
                        foreach (var hint in row.Children.OfType<TextBlock>().Where(t => t.Classes.Contains("hint")))
                            Assert.True(hint.Bounds.Top < text.Bounds.Bottom && text.Bounds.Top < hint.Bounds.Bottom,
                                $"Caption below heading: {text.Text}");
                }
            }
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Screenshots_web_service_and_video_status(bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            Directory.CreateDirectory(Settings.VideoDirectory(harness.DataDirectory));
            foreach (var (uid, file) in new[] { (12345, "小鹿的欢迎动画.mp4"), (67890, "星光入场.webm") })
            {
                Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, uid, file);
                File.WriteAllText(Path.Combine(Settings.VideoDirectory(harness.DataDirectory), file), "video");
            }
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["welcome_animation.enabled"] = "true" });
            var viewModel = harness.CreateViewModel(new FakeTimeProvider());
            var window = new MainWindow(harness.DataDirectory)
            {
                DataContext = viewModel, Width = 1200, Height = 720,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await viewModel.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 6;
                GuiHarness.Flush();
                foreach (var (name, state) in new[]
                {
                    ("connected", new WebServiceStatus(true, 17890, 2, 1)),
                    ("partial", new WebServiceStatus(true, 17890, 1, 0)),
                    ("setup", new WebServiceStatus(true, 17890, 0, 0)),
                    ("occupied", new WebServiceStatus(false, 17890, 0, 0, "端口 17890 被别的程序占用"))
                })
                {
                    viewModel.WebService.Apply(state);
                    Save(window, $"web-service-N{(dark ? 2 : 1)}-{name}");
                }
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 1, 1));
                window.Navigation.SelectedIndex = 3;
                viewModel.WebService.Apply(new WelcomeAnimationPlayback("one", 12345, "小鹿的欢迎动画.mp4", true, PlaybackState.Completed));
                Save(window, $"welcome-animation-N3-{(dark ? "dark" : "light")}");
            }
            finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(1040, false)]
    [InlineData(1040, true)]
    [InlineData(1200, false)]
    [InlineData(1200, true)]
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
    [InlineData(1200, false)]
    [InlineData(1200, true)]
    [InlineData(1040, false)]
    [InlineData(1040, true)]
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
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
    public Task Screenshots_welcome_animation_states(int width, int height, bool dark)
    {
        Assert.SkipWhen(HeadlessApp.ScreenshotDirectory is null, "Set AETHER_SCREENSHOT_DIR to render screenshots.");
        return HeadlessApp.Session.Dispatch(async () =>
        {
            using var harness = new GuiHarness();
            await harness.SaveCredentialAsync();
            var videos = new Dictionary<long, string> { [1234567890123456] = "小鹿的欢迎动画.mp4", [67890] = "星光入场.webm", [24680] = "很长的专属欢迎动画文件名.mp4", [13579] = "找不到的视频.mp4" };
            Directory.CreateDirectory(Path.Combine(harness.DataDirectory, "videos"));
            foreach (var (uid, file) in videos)
            {
                Settings.SaveWelcomeAnimationVideo(harness.DataDirectory, uid, file);
                if (uid != 13579) File.WriteAllText(Path.Combine(harness.DataDirectory, "videos", file), "video");
            }
            Settings.Save(harness.DataDirectory, new Dictionary<string, string> { ["welcome_animation.enabled"] = "true" });
            var viewModel = harness.CreateViewModel(new FakeTimeProvider());
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
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 2, 0));
                foreach (var (state, index) in new[] { PlaybackState.Completed, PlaybackState.Failed, PlaybackState.NoReport, PlaybackState.Skipped, PlaybackState.Queued }.Select((state, index) => (state, index)))
                    viewModel.WebService.Apply(new WelcomeAnimationPlayback(index.ToString(), index == 1 ? 67890 : 24680, index == 1 ? "星光入场.webm" : "很长的专属欢迎动画文件名.mp4", index == 0, state, state == PlaybackState.Skipped ? "上一个还没播完" : null,
                        Nickname: index switch { 1 => "柚子不酸", 2 => "bili_6508617762", _ => null }));
                Capture("connected");
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 0, 0));
                Capture("setup");
                viewModel.WebService.Apply(new WebServiceStatus(false, 17890, 0, 0, "端口 17890 被别的程序占用"));
                Capture("port-occupied");
                viewModel.WebService.Apply(new WebServiceStatus(true, 17890, 1, 0));
                viewModel.WebService.Apply(new WelcomeAnimationPlayback("playing", 1234567890123456, videos[1234567890123456], true, PlaybackState.Playing));
                Capture("playing");
                viewModel.WebService.Apply(new WelcomeAnimationPlayback("playing", 1234567890123456, videos[1234567890123456], true, PlaybackState.Stopped));
                Capture("stopped");
                viewModel.WebService.Apply(new WelcomeAnimationPlayback("real", 1234567890123456, videos[1234567890123456], false, PlaybackState.Playing));
                Capture("entry-playing");
                var rows = page.FindControl<StackPanel>("AnimationRows")!.Children.Cast<WelcomeAnimationRow>().ToArray();
                rows[0].FindControl<TextBox>("UidInput")!.Text = "";
                rows[1].FindControl<TextBox>("UidInput")!.Text = "not-a-uid";
                rows[2].FindControl<TextBox>("UidInput")!.Text = "13579";
                Capture("row-errors");
                // Reload after deleting the fixture mappings to show the first-use state.
                foreach (var row in rows) row.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                page.FindControl<ToggleSwitch>("AnimationInput")!.IsChecked = false;
                GuiHarness.FindVisual<Button>(page, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await GuiHarness.WaitForAsync(() => page.IsEnabled);
                viewModel.WebService.RecentAnimations.Clear();
                GuiHarness.FindVisual<TextBlock>(page, "Message").Text = "";
                viewModel.WebService.Apply(new WebServiceStatus(false, 17890, 0, 0));
                GuiHarness.Flush();
                // Settle the transition, then redraw its final edges instead of reusing an intermediate frame.
                await Task.Delay(300, TestContext.Current.CancellationToken);
                foreach (var visual in page.FindControl<ToggleSwitch>("AnimationInput")!.GetVisualDescendants())
                    visual.InvalidateVisual();
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
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    [InlineData(1040, 600, false)]
    [InlineData(1040, 600, true)]
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
                await viewModel.ReadAccountCommand.ExecutionTask!;
                foreach (var (pageIndex, name) in new[] { (6, "LogLevelInput"), (2, "GuardInput") })
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
                await viewModel.ReadAccountCommand.ExecutionTask!;
                window.Navigation.SelectedIndex = 5;
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
                window.Navigation.SelectedIndex = 6;
                window.Navigation.SelectedIndex = 5;
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
            var pages = new[] { "danmaku", "blind-box", "welcome", "welcome-animation", "lottery", "send-queue", "general" };
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
            GuiHarness.FindVisual<Button>(welcome, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            navigation.SelectedIndex = 6;
            GuiHarness.Flush();
            var general = window.GetVisualDescendants().OfType<SettingsPage>()
                .Single(page => page.Section == SettingsSection.General);
            var fileMessage = GuiHarness.FindVisual<TextBlock>(general, "FileMessage");
            Assert.Contains(harness.DataDirectory, fileMessage.Text);
            // Keep the real parse error while removing the per-run temporary directory from its fixture text.
            fileMessage.Text = fileMessage.Text!.Replace(harness.DataDirectory, "/aether-visual-review");
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
        foreach (var page in topLevel.GetVisualDescendants().OfType<SettingsPage>().Where(p => p.IsEffectivelyVisible))
        {
            // Verify visible ink as well as layout metrics on the uniform page header.
            var caption = GuiHarness.FindVisual<TextBlock>(page, "TimingHint");
            var origin = caption.TranslatePoint(default, topLevel)!.Value;
            using var pixels = bitmap.Lock();
            Assert.Equal(32, pixels.Format.BitsPerPixel);
            var colors = new HashSet<int>();
            for (var y = (int)origin.Y; y < origin.Y + caption.Bounds.Height; y++)
                for (var x = (int)origin.X; x < origin.X + caption.Bounds.Width; x++)
                    colors.Add(Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4));
            Assert.True(colors.Count > 1, $"Caption not drawn: {fileName}");
        }
        Directory.CreateDirectory(HeadlessApp.ScreenshotDirectory!);
        bitmap.Save(Path.Combine(HeadlessApp.ScreenshotDirectory!, fileName + ".png"), PngBitmapEncoderOptions.Default);
    }
}
