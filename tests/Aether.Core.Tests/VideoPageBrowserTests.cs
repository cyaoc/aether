using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class VideoPageBrowserTests
{
    [Fact]
    public async Task Stopping_a_test_play_pauses_and_clears_the_real_video_page_and_it_can_play_again()
    {
        await using var h = new WatchHarness();
        Directory.CreateDirectory(Settings.VideoDirectory(h.DataDirectory));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "welcome.mp4"), Path.Combine(Settings.VideoDirectory(h.DataDirectory), "welcome.mp4"));
        await using var browser = await ChromeBrowser.OpenVideoPageAsync(h, new Dictionary<string, string>
        {
            ["entry_animation.enabled"] = "true", ["entry_animation_viewers.10001"] = "welcome.mp4"
        });
        await h.WaitForLogAsync("视频页 1 个推流软件");
        // Keep the short fixture playing until Stop; muting removes the headless browser's autoplay gate.
        await browser.EvaluateAsync("document.querySelector('video').muted = true; document.querySelector('video').loop = true;");
        await using var updates = h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await h.Client.WebService.TestPlayWelcomeAsync(10001);
            SourcePlayback playing;
            do { Assert.True(await updates.MoveNextAsync()); }
            while (updates.Current is not SourcePlayback { State: PlaybackState.Playing });
            playing = (SourcePlayback)updates.Current;
            await browser.WaitAsync("document.querySelector('video').style.visibility === 'visible' && !document.querySelector('video').paused");
            await h.Client.WebService.StopTestPlayAsync(playing.Id);
            await browser.WaitAsync("document.querySelector('video').style.visibility === 'hidden' && document.querySelector('video').paused && !document.querySelector('video').hasAttribute('src')");
            do { Assert.True(await updates.MoveNextAsync()); }
            while (updates.Current is not SourcePlayback { State: PlaybackState.Stopped });
            Assert.Equal(playing.Id, ((SourcePlayback)updates.Current).Id);
        }
        Assert.Empty(browser.Errors);
    }
}
