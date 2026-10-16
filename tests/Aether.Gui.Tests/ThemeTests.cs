using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class ThemeTests
{
    [Fact]
    public Task Switches_are_named_show_no_on_off_text_save_with_their_page_and_disable_for_file_errors() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            var retention = Assert.Single(window.GetVisualDescendants().OfType<ToggleSwitch>(), x => x.IsEffectivelyVisible);
            Assert.Equal("只保留最近 1000 条", AutomationProperties.GetName(retention));
            retention.IsChecked = false;
            Assert.False(harness.LoadSettings().KeepRecentDanmaku);
            var navigation = window.FindControl<ListBox>("Navigation")!;
            foreach (var index in new[] { 1, 2 })
            {
                navigation.SelectedIndex = index;
                GuiHarness.Flush();
                var page = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible);
                Assert.Contains("没有未保存的修改", GuiHarness.VisibleText(window));
                var before = File.ReadAllText(Settings.FilePath(harness.DataDirectory));
                var switches = page.GetVisualDescendants().OfType<ToggleSwitch>().Where(x => x.IsEffectivelyVisible).ToArray();
                Assert.Equal(index == 1 ? 2 : 1, switches.Length);
                foreach (var toggle in switches)
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)));
                    Assert.True(string.IsNullOrEmpty(toggle.OnContent?.ToString()));
                    Assert.True(string.IsNullOrEmpty(toggle.OffContent?.ToString()));
                    toggle.IsChecked = !toggle.IsChecked;
                }
                Assert.Equal(before, File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
                Assert.Contains("有未保存的修改", GuiHarness.VisibleText(window));
                page.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.NotEqual(before, File.ReadAllText(Settings.FilePath(harness.DataDirectory)));
                Assert.Contains("没有未保存的修改", GuiHarness.VisibleText(window));
            }
            File.WriteAllText(Settings.FilePath(harness.DataDirectory), "log:\n  level: @bad\n");
            foreach (var index in new[] { 1, 2 })
            {
                navigation.SelectedIndex = index;
                GuiHarness.Flush();
                Assert.All(window.GetVisualDescendants().OfType<ToggleSwitch>().Where(x => x.IsEffectivelyVisible), x => Assert.False(x.IsEffectivelyEnabled));
            }
            navigation.SelectedIndex = 5;
            GuiHarness.Flush();
            var openFile = Assert.Single(window.GetVisualDescendants().OfType<Button>(), x => x.IsEffectivelyVisible && Equals(x.Content, "打开设置文件"));
            Assert.True(openFile.IsEffectivelyEnabled);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Button_text_is_centered_in_taller_buttons() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            window.FindControl<ListBox>("Navigation")!.SelectedIndex = 1;
            GuiHarness.Flush();
            var save = window.GetVisualDescendants().OfType<SettingsPage>().Single(x => x.IsEffectivelyVisible).FindControl<Button>("SaveButton")!;
            var text = save.GetVisualDescendants().OfType<TextBlock>().Single();
            var center = text.TranslatePoint(new Point(0, text.TextLayout.Height / 2), save)!.Value.Y;
            Assert.InRange(center, save.Bounds.Height / 2 - 1, save.Bounds.Height / 2 + 1);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Selection_hover_and_theme_changes_preserve_list_and_avatar_colors(bool dark) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "夜航船", "晚上好") { Uid = 999 });
            GuiHarness.Flush();
            var row = Assert.IsType<ListBoxItem>(window.DanmakuList.ContainerFromIndex(0));
            var avatar = row.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "Avatar");
            var original = ColorOf(avatar.Background);
            var origin = row.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(origin);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseUp(origin, MouseButton.Left);
            GuiHarness.Flush();
            Assert.True(row.IsSelected);
            Assert.Equal(Colors.Transparent, ColorOf(row.GetVisualDescendants().OfType<ContentPresenter>().First().Background));
            Assert.Equal(original, ColorOf(avatar.Background));
            var nav = window.FindControl<ListBox>("Navigation")!;
            var selected = Assert.IsType<ListBoxItem>(nav.ContainerFromIndex(0));
            var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
            var navOrigin = selected.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(navOrigin);
            window.MouseDown(navOrigin, MouseButton.Left);
            GuiHarness.Flush();
            Assert.Equal(dark ? Color.Parse("#262F52") : Color.Parse("#E6EBFA"), ColorOf(presenter.Background));
            var counter = selected.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Classes.Contains("muted"));
            Assert.True(Contrast(ColorOf(counter.Foreground), ColorOf(presenter.Background)) >= 4.5);
            window.MouseUp(navOrigin, MouseButton.Left);
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            GuiHarness.Flush();
            Assert.NotEqual(original, ColorOf(avatar.Background));
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            GuiHarness.Flush();
            Assert.Equal(original, ColorOf(avatar.Background));
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Guard_rows_and_badges_keep_readable_contrast_across_theme_changes() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var harness = new GuiHarness();
        await harness.SaveCredentialAsync();
        var viewModel = harness.CreateViewModel();
        var window = new MainWindow(harness.DataDirectory) { DataContext = viewModel };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            for (var i = 0; i < 4; i++)
                viewModel.Apply(new Danmaku(DateTimeOffset.UnixEpoch, "观众", "晚上好")
                    { Uid = i == 0 ? 123 : 1000 + i, GuardTier = i, FanClubLevel = 47, WealthLevel = 45 });
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                GuiHarness.Flush();
                for (var i = 0; i < 4; i++)
                {
                    var row = Assert.IsType<ListBoxItem>(window.DanmakuList.ContainerFromIndex(i));
                    var borders = row.GetVisualDescendants().OfType<Border>().ToArray();
                    var background = borders.Single(x => x.Name == "DanmakuRow");
                    var timestamp = row.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "00:00:00");
                    Assert.True(Contrast(ColorOf(timestamp.Foreground), ColorOf(background.Background)) >= 4.5,
                        $"{theme}, row {i}: timestamp contrast");
                    foreach (var badge in borders.Where(x => x.Classes.Contains("badge") && x.IsEffectivelyVisible))
                    {
                        var text = Assert.IsType<TextBlock>(badge.Child);
                        Assert.True(Contrast(ColorOf(text.Foreground), ColorOf(badge.Background)) >= 4.5,
                            $"{theme}, {text.Text}: badge contrast");
                    }
                }
            }
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); Dispatcher.UIThread.RunJobs(); }
        return true;
    }, TestContext.Current.CancellationToken);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
