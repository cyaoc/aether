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
            panel.Select(1);
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
}
