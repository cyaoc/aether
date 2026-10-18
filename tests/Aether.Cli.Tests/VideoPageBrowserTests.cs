using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class VideoPageBrowserTests
{
    [Fact]
    public async Task Stopping_a_test_pauses_and_clears_the_production_video_and_it_can_play_again()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AETHER_TEST_CHROME")))
            Assert.Skip("Set AETHER_TEST_CHROME for real video browser acceptance.");
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["web_service.port"] = Aether.Core.Tests.WelcomeAnimationTests.FreePort().ToString()
        });
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "welcome.mp4"), Path.Combine(Settings.VideoDirectory(h.DataDirectory), "welcome.mp4"));
        Settings.SaveWelcomeAnimationVideo(h.DataDirectory, 10001, "welcome.mp4");
        await h.Client.ApplyWebSettingsAsync(Settings.Load(h.DataDirectory));
        await using var browser = new LotteryBrowser();
        await browser.OpenAsync(h.DataDirectory, h.Client.WebService.Status.VideoAddress);
        await h.WaitForLogAsync("视频页 1 个推流软件");
        // Keep the short fixture playing until Stop; muting removes the headless browser's autoplay gate.
        await browser.EvaluateAsync("document.querySelector('video').muted = true; document.querySelector('video').loop = true;");
        await using var updates = h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        for (var attempt = 0; attempt < 2; attempt++)
        {
            h.Client.WelcomeAnimation.TestPlay(10001);
            WelcomeAnimationPlayback playing;
            do { Assert.True(await updates.MoveNextAsync()); }
            while (updates.Current is not WelcomeAnimationPlayback { State: PlaybackState.Playing });
            playing = (WelcomeAnimationPlayback)updates.Current;
            await browser.WaitAsync("document.querySelector('video').style.visibility === 'visible' && !document.querySelector('video').paused");
            h.Client.WelcomeAnimation.StopTestPlay(playing.Id);
            await browser.WaitAsync("document.querySelector('video').style.visibility === 'hidden' && document.querySelector('video').paused && !document.querySelector('video').hasAttribute('src')");
            do { Assert.True(await updates.MoveNextAsync()); }
            while (updates.Current is not WelcomeAnimationPlayback { State: PlaybackState.Stopped });
            Assert.Equal(playing.Id, ((WelcomeAnimationPlayback)updates.Current).Id);
        }
        Assert.Empty(browser.Errors);
    }
}
