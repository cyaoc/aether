using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class EntryAnimationSettingsTests
{
    [Fact]
    public Task Volume_slider_is_named_and_steps_by_one_and_ten_from_the_keyboard() => HeadlessApp.Session.Dispatch(() =>
    {
        using var h = new GuiHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.volume"] = "30" });
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var volume = GuiHarness.FindVisual<Slider>(page, "VolumeInput");
            Assert.Equal("动画音量", AutomationProperties.GetName(volume));
            volume.Focus();
            foreach (var (key, physical, expected) in new[] { (Key.Right, PhysicalKey.ArrowRight, 31), (Key.PageUp, PhysicalKey.PageUp, 41),
                (Key.Left, PhysicalKey.ArrowLeft, 40), (Key.PageDown, PhysicalKey.PageDown, 30), (Key.Home, PhysicalKey.Home, 0), (Key.End, PhysicalKey.End, 100) })
            {
                window.KeyPress(key, RawInputModifiers.None, physical, null);
                Assert.Equal(expected, volume.Value);
            }
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Volume_slider_shows_and_saves_entry_animation_volume_with_zero_as_the_far_left() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.volume"] = "30" });
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var volume = GuiHarness.FindVisual<Slider>(page, "VolumeInput");
            Assert.Equal(30, volume.Value);
            volume.Value = 65;
            GuiHarness.Flush();
            Assert.True(page.CanSave);
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal(65, Settings.Load(h.DataDirectory).EntryAnimationVolume.Value);
            Assert.False(page.CanSave);
            volume.Value = 0;
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal(0, Settings.Load(h.DataDirectory).EntryAnimationVolume.Value);
            var reopened = new AnimationSettingsPage(h.DataDirectory);
            window.Content = reopened;
            GuiHarness.Flush();
            Assert.Equal(0, GuiHarness.FindVisual<Slider>(reopened, "VolumeInput").Value);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Volume_slider_jumps_to_where_the_track_is_pressed_and_follows_the_thumb_to_whole_numbers() => HeadlessApp.Session.Dispatch(() =>
    {
        using var h = new GuiHarness();
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var volume = GuiHarness.FindVisual<Slider>(page, "VolumeInput");
            var track = GuiHarness.FindVisual<Track>(volume, "PART_Track");
            Point At(double x) => track.TranslatePoint(new Point(x, track.Bounds.Height / 2), window)!.Value;
            window.MouseDown(At(0), MouseButton.Left);
            window.MouseUp(At(0), MouseButton.Left);
            Assert.Equal(0, volume.Value);
            var thumb = volume.GetVisualDescendants().OfType<Thumb>().Single();
            var grip = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            window.MouseDown(grip, MouseButton.Left);
            window.MouseMove(At(track.Bounds.Width * .4321));
            window.MouseUp(At(track.Bounds.Width * .4321), MouseButton.Left);
            Assert.InRange(volume.Value, 40, 45);
            Assert.Equal(Math.Round(volume.Value), volume.Value);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    // The test bar needs 350-odd of the 476 a 1040-wide window leaves beside the recent list, so the slider takes what is left.
    [Theory]
    [InlineData(1200, true)]
    [InlineData(1040, false)]
    public Task Volume_slider_ends_the_test_row_and_gives_way_to_the_test_bar_when_the_window_is_narrow(int width, bool full) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        await h.SaveCredentialAsync();
        var viewModel = h.CreateViewModel();
        var window = new MainWindow(h.DataDirectory) { DataContext = viewModel, Width = width, Height = 720 };
        window.Show();
        try
        {
            await viewModel.ReadAccountCommand.ExecutionTask!;
            window.Navigation.SelectedIndex = 2;
            window.GetVisualDescendants().OfType<WelcomePanel>().Single().Select(1);
            GuiHarness.Flush();
            var page = window.GetVisualDescendants().OfType<AnimationSettingsPage>().Single();
            var volume = GuiHarness.FindVisual<Slider>(page, "VolumeInput");
            var track = GuiHarness.FindVisual<Track>(volume, "PART_Track");
            var speaker = volume.GetVisualDescendants().OfType<Viewbox>().Single();
            var play = GuiHarness.FindVisual<Button>(page, "PlayButton");
            var contentEdge = GuiHarness.FindVisual<Grid>(page, "EmptyAnimations");
            double Left(Control control) => control.TranslatePoint(default, window)!.Value.X;
            double Right(Control control) => Left(control) + control.Bounds.Width;
            Assert.Equal(Right(contentEdge), Right(track), 0.5);
            Assert.True(Right(play) <= Left(speaker));
            if (full) Assert.Equal(180, track.Bounds.Width, 0.5);
            else Assert.InRange(track.Bounds.Width, 40, 179);
        }
        finally { await viewModel.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_multi_line_volume_leaves_the_slider_off_and_points_to_the_settings_file() => HeadlessApp.Session.Dispatch(() =>
    {
        using var h = new GuiHarness();
        Directory.CreateDirectory(h.DataDirectory);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "entry_animation:\n  volume: |-\n    80\n");
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            Assert.False(GuiHarness.FindVisual<Slider>(page, "VolumeInput").IsEnabled);
            Assert.True(GuiHarness.FindVisual<TextBlock>(page, "VolumeReadOnly").IsVisible);
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(0, "仅按下方指定观众设置播放")]
    [InlineData(30, "达到门槛后，按实际等级播放对应档位")]
    [InlineData(40, "达到门槛后，按实际等级播放对应档位")]
    [InlineData(50, "达到门槛后，按实际等级播放对应档位")]
    [InlineData(60, "达到门槛后，按实际等级播放对应档位")]
    public Task Fan_club_hint_follows_selection_and_saved_settings(int level, string hint) => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["entry_animation.fan_club_level"] = level.ToString() });
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1040, Height = 600 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            var input = GuiHarness.FindVisual<ComboBox>(page, "FanClubInput");
            Assert.Equal(level, input.SelectedItem);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == hint);
            input.SelectedItem = 0;
            GuiHarness.Flush();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "仅按下方指定观众设置播放");
            input.SelectedItem = 40;
            GuiHarness.Flush();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "达到门槛后，按实际等级播放对应档位");
            input.SelectedItem = level == 0 ? 30 : 0;
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            Assert.Equal(level == 0 ? 30 : 0, Settings.Load(h.DataDirectory).EntryAnimationFanClubLevel.Value);
            var reopened = new AnimationSettingsPage(h.DataDirectory);
            window.Content = reopened;
            GuiHarness.Flush();
            Assert.Equal(level == 0 ? 30 : 0, GuiHarness.FindVisual<ComboBox>(reopened, "FanClubInput").SelectedItem);
            Assert.Contains(reopened.GetVisualDescendants().OfType<TextBlock>(), text => text.Text ==
                (level == 0 ? "达到门槛后，按实际等级播放对应档位" : "仅按下方指定观众设置播放"));
        }
        finally { window.Close(); GuiHarness.Flush(); }
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Animation_list_saves_tier_video_and_off_and_tabs_preserve_drafts() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.WriteAllText(Path.Combine(Settings.VideoDirectory(h.DataDirectory), "welcome.webm"), "video");
        Settings.Save(h.DataDirectory, new Dictionary<string, string> {
            ["entry_animation_viewers.1"] = "30", ["entry_animation_viewers.2"] = "0", ["entry_animation_viewers.3"] = "welcome.webm" });
        var panel = new WelcomePanel(h.DataDirectory);
        var window = new Window { Content = panel, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            panel.Select(1);
            GuiHarness.Flush();
            var page = panel.Animation;
            page.Animations[0].Choice = "50";
            page.Animations[1].Choice = "不播";
            panel.Select(0);
            Assert.True(panel.Danmaku.IsVisible);
            Assert.False(page.IsVisible);
            Assert.Equal("下次连接直播间时生效", panel.Danmaku.TimingHint);
            panel.Select(1);
            Assert.False(panel.Danmaku.IsVisible);
            Assert.True(page.IsVisible);
            Assert.Equal("保存后，下一次进场生效", page.TimingHint);
            Assert.Equal(1, ((ListBox)panel.Danmaku.HeaderTabs!).SelectedIndex);
            Assert.Equal("50", page.Animations[0].Choice);
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(page, "PART_SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingSave;
            var saved = Settings.Load(h.DataDirectory);
            Assert.Null(saved.Error);
            Assert.Equal(50, saved.EntryAnimationViewers[1].Value.Tier?.Value);
            Assert.Equal(AnimationChoice.Off, saved.EntryAnimationViewers[2].Value);
            Assert.Equal("welcome.webm", saved.EntryAnimationViewers[3].Value.Video);
            Assert.False(page.ShowReconnectNotice);
        }
        finally { window.Close(); GuiHarness.Flush(); }
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Test_bar_needs_a_page_and_a_nickname_not_the_switch_and_stops_its_own_play() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var vm = h.CreateViewModel();
        var panel = new WelcomePanel(h.DataDirectory) { DataContext = vm };
        var window = new Window { Content = panel, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            panel.Select(1);
            GuiHarness.Flush();
            var page = panel.Animation;
            Assert.False(Settings.Load(h.DataDirectory).EntryAnimationEnabled.Value);
            var play = GuiHarness.FindVisual<Button>(page, "PlayButton");
            Assert.False(play.IsEnabled);
            vm.WebService.Apply(new WebServiceStatus(true, 17890, 1, 0, 0));
            Assert.True(play.IsEnabled);
            Assert.Equal(60, Assert.IsType<EntryTier>(GuiHarness.FindVisual<ComboBox>(page, "TierInput").SelectedItem).Value);
            var nickname = GuiHarness.FindVisual<TextBox>(page, "NicknameInput");
            nickname.Text = " ";
            GuiHarness.Flush();
            Assert.False(play.IsEnabled);
            nickname.Text = "柚子不酸";
            GuiHarness.Flush();
            Assert.True(play.IsEnabled);
            vm.WebService.Apply(new SourcePlayback("one", 0, "60 档", true, PlaybackState.Playing, Source: PlaybackSource.Entry, TriggerReason: "柚子不酸"));
            Assert.Equal("停止", play.Content);
            Assert.True(play.IsEnabled);
            Assert.Equal("60 档 · 柚子不酸", Assert.Single(vm.WebService.RecentEntryAnimations).ContentDetail);
        }
        finally { await vm.ShutdownAsync(); window.Close(); GuiHarness.Flush(); }
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task A_viewer_without_a_video_yet_asks_for_one_rather_than_reporting_it_missing() => HeadlessApp.Session.Dispatch(async () =>
    {
        using var h = new GuiHarness();
        var page = new AnimationSettingsPage(h.DataDirectory);
        var window = new Window { Content = page, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            GuiHarness.Flush();
            GuiHarness.FindVisual<Button>(page, "AddAnimationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GuiHarness.Flush();
            var row = Assert.Single(page.Animations);
            row.UidText = "42";
            row.Choice = "欢迎动画";
            GuiHarness.Flush();
            Assert.Equal("选一个视频", row.FileText);
            Assert.False(row.Missing);
            Assert.Equal("", row.RowMessage);
            Assert.False(row.CanTest);
            Assert.False(page.CanSave);
        }
        finally { window.Close(); GuiHarness.Flush(); await Task.CompletedTask; }
    }, TestContext.Current.CancellationToken);
}
