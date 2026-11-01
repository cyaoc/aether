using Aether.Core;
using Aether.Gui.Tests.Support;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Aether.Gui.Tests;

public sealed class EntryAnimationSettingsTests
{
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
            vm.WebService.Apply(new WebServiceStatus(true, 17890, 1, 0));
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
