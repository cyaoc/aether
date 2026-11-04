using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>The 礼物榜页 in a real browser: gifts pushed through the room reach it as SSE states. The page's clock is an
/// animation, so most of these tests stop it and step it by hand; a state that arrives while it is stopped starts its event at
/// that time.</summary>
public sealed partial class GiftBoardBrowserTests
{
    [Theory]
    [InlineData(480, 1000, true, 9, 6, 1.0)]
    [InlineData(600, 1250, true, 9, 6, 1.25)]
    [InlineData(480, 1000, false, 12, 9, 1.0)]
    [InlineData(480, 640, true, 9, 3, 640.0 / 820)]
    public async Task The_board_scales_by_the_page_width_and_shows_the_rows_its_height_allows(int width, int height, bool mascot, int viewers, int fit, double scale)
    {
        await using var page = await StartAsync(("gift_board.mascot", mascot ? "true" : "false"), ("gift_board.volume", "0"));
        await page.OpenAsync(width, height);
        await page.PauseClockAsync();
        await page.GiveToAsync(viewers);
        await page.SettleAsync();
        var left = (width - 480 * scale) / 2;
        var top = mascot ? Mascot : NoMascot;
        var edges = await page.OpaqueEdgesAsync((int)((top + CardsY + 49) * scale), width);
        Assert.InRange(edges.Left, left + 20 * scale - 4, left + 20 * scale + 4);
        Assert.InRange(edges.Right, left + 460 * scale - 4, left + 460 * scale + 4);
        // Where the rows fit they show in a column between the rank number and the avatar; a list too long for the page scrolls.
        if (viewers - 3 > fit) return;
        var pills = Enumerable.Range(0, viewers - 3).Select(row => ((int)(left + 100 * scale), (int)((top + RowsY + row * Pitch + 25) * scale)));
        Assert.All(await page.AlphaAsync(pills), alpha => Assert.True(alpha > 100));
    }

    [Fact]
    public async Task The_four_animations_follow_what_changed_and_only_a_new_first_place_sounds()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        // Three viewers appear, the first adds to the first place, the third climbs past the second and then takes first place.
        var steps = new (long Uid, long Num, string Event)[] { (1, 1, "new"), (2, 1, "new"), (3, 1, "new"), (1, 1, "more"), (3, 2, "pass"), (3, 4, "top") };
        foreach (var (uid, num, expected) in steps)
        {
            await page.GiveAsync(uid, 4000 - uid * 1000, num);
            await page.WaitForEventAsync(expected);
            if (expected == "top") foreach (var time in new[] { 300, 500, 600, 700, 800, 1000, 1500, 2500 }) await page.SetEventTimeAsync(time);
            await page.SettleAsync();
            Assert.Equal(expected == "top" ? 1 : 0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
        }
        var sound = await page.Browser.EvaluateAsync("sounds[0]");
        // The sound is the sparkle, and its loudest moment is heard 950 ms after the event started. The audio clock moves on a few
        // milliseconds between the page reading it and the sound starting, which the measure cannot tell from an early sound.
        Assert.InRange(sound.GetProperty("peakAfterEventStart").GetDouble(), 0.93, 0.951);
        Assert.InRange(sound.GetProperty("length").GetDouble(), 2.5, 3);
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_volume_of_zero_loads_no_sound_and_plays_none()
    {
        await using var page = await StartAsync(("gift_board.volume", "0"));
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        await page.GiveAsync(1, 3000);
        await page.GiveAsync(2, 1000);
        await page.SettleAsync();
        await page.GiveAsync(2, 1000, 5);
        await page.WaitForEventAsync("top");
        foreach (var time in new[] { 600, 700, 800, 1500 }) await page.SetEventTimeAsync(time);
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
        Assert.DoesNotContain(page.Browser.Requests, url => url.EndsWith(".ogg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_backlog_of_more_than_three_jumps_to_the_latest_state_and_the_changes_between_are_not_played()
    {
        await using var page = await StartAsync(("gift_board.volume", "0"));
        await page.OpenAsync(480, 1000);
        await page.PauseClockAsync();
        await page.GiveToAsync(5);
        await page.WaitForEventAsync("new");
        await page.StepClockAsync(1700);
        // The first change has played; the four behind it became one redraw of the latest board instead of four more animations.
        Assert.Equal("", await page.PlayingAsync());
        Assert.All(await page.AlphaAsync([(100, (int)(Mascot + RowsY + 25)), (100, (int)(Mascot + RowsY + Pitch + 25))]), alpha => Assert.True(alpha > 100));
    }

    [Fact]
    public async Task The_first_state_is_drawn_whole_without_animation_or_sound_and_a_board_that_changes_shape_is_redrawn()
    {
        await using var page = await StartAsync();
        await page.GiveToAsync(5);
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        // Five viewers were on the board before the page opened: it shows them at once.
        Assert.Equal("", await page.PlayingAsync());
        Assert.All(await page.AlphaAsync([(100, (int)(Mascot + RowsY + 25)), (100, (int)(Mascot + RowsY + Pitch + 25))]), alpha => Assert.True(alpha > 100));

        // Switching the mascot off lifts the board; that is a redraw, not an animation, and it needs no clock step.
        await page.H.ApplySettingsAsync(("gift_board.mascot", "false"));
        await page.WaitForSolidAsync(100, (int)(NoMascot + RowsY + 25));
        Assert.Equal("", await page.PlayingAsync());
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    [Fact]
    public async Task Effects_are_drawn_inside_the_board_and_never_beyond_it()
    {
        await using var page = await StartAsync(("gift_board.volume", "0"));
        await page.OpenAsync(480, 640);
        await page.PauseClockAsync();
        foreach (var uid in new long[] { 1, 2, 3 })
        {
            await page.GiveAsync(uid, 4000 - uid * 1000);
            await page.SettleAsync();
        }
        // The board is 0.78 of its width and centred, so its sides leave 52 px of margin that the rays and confetti would reach.
        var margins = Enumerable.Range(0, 640 / 8).SelectMany(row => MarginColumns.Select(x => (x, row * 8))).ToList();
        Assert.All(await page.AlphaAsync(margins), alpha => Assert.Equal(0, alpha));
        await page.GiveAsync(2, 2000, 3);
        await page.WaitForEventAsync("top");
        foreach (var time in new[] { 1100, 1500, 2000, 2600, 3100 })
        {
            await page.SetEventTimeAsync(time);
            Assert.All(await page.AlphaAsync(margins), alpha => Assert.Equal(0, alpha));
        }
    }

    [Fact]
    public async Task Pictures_come_from_the_bilibili_host_without_a_referrer_and_a_missing_one_does_not_stop_the_page()
    {
        await using var page = await StartAsync(("gift_board.volume", "0"));
        await page.OpenAsync(480, 1000);
        await page.Browser.CommandAsync("Network.setBlockedURLs", new { urls = BilibiliPictureHosts });
        await page.Browser.EvaluateAsync("window.pictures = []; const Original = Image; window.Image = class extends Original { constructor() { super(); pictures.push(this); } }; true");
        await page.PauseClockAsync();
        await page.GiveAsync(1, 3000);
        await page.Browser.WaitAsync("pictures.length >= 2");
        await page.SettleAsync();
        var pictures = await page.Browser.EvaluateAsync("pictures.map(picture => [new URL(picture.src).hostname, picture.referrerPolicy, picture.crossOrigin])");
        Assert.Equal(["i0.hdslb.com", "no-referrer", "anonymous"], pictures[0].EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(pictures.EnumerateArray(), picture => picture[0].GetString() == "s1.hdslb.com");
        // Neither picture arrived, and the next viewer's animation still plays.
        await page.GiveAsync(2, 2000);
        await page.WaitForEventAsync("new");
        Assert.All(page.Browser.Requests, url => Assert.True(new Uri(url).Host == "localhost" || new Uri(url).Host.EndsWith(".hdslb.com", StringComparison.Ordinal), url));
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_viewer_without_a_picture_gets_a_disc_with_the_first_character_of_the_name()
    {
        await using var page = await StartAsync(("gift_board.volume", "0"));
        await page.OpenAsync(480, 1000);
        await page.PauseClockAsync();
        var gift = JsonNode.Parse(BlindGiftFixture.Ordinary(uid: 1, nickname: "月下Lily"))!;
        gift["data"]!["face"] = "";
        await page.PushAsync(gift.ToJsonString());
        await page.WaitForEventAsync("new");
        await page.SettleAsync();
        // Left of the avatar's middle the disc is solid; the translucent card on its own never is.
        Assert.Equal(255, (await page.AlphaAsync([(126, (int)(Mascot + CardsY + 49))]))[0]);
    }
}
