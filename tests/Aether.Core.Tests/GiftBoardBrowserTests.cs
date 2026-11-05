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
        await using var page = await StartAsync(options => options with { Mascot = mascot, Volume = 0 });
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
        // The first viewer appears in first place, two more below, the first adds to the first place, the third climbs past the
        // second and then takes first place.
        var steps = new (long Uid, long Num, string Event)[] { (1, 1, "top"), (2, 1, "new"), (3, 1, "new"), (1, 1, "more"), (3, 2, "pass"), (3, 4, "top") };
        var heard = 0;
        foreach (var (uid, num, expected) in steps)
        {
            await page.GiveAsync(uid, 4000 - uid * 1000, num);
            await page.WaitForEventAsync(expected);
            if (expected == "top") foreach (var time in new[] { 300, 500, 600, 700, 800, 1000, 1500, 2500 }) await page.SetEventTimeAsync(time);
            await page.SettleAsync();
            if (expected == "top") heard++;
            Assert.Equal(heard, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
        }
        // Each sound is the sparkle, and its loudest moment is heard 950 ms after the event started. The audio clock moves on a few
        // milliseconds between the page reading it and the sound starting, which the measure cannot tell from an early sound.
        foreach (var index in new[] { 0, 1 })
        {
            var sound = await page.Browser.EvaluateAsync($"sounds[{index}]");
            Assert.InRange(sound.GetProperty("peakAfterEventStart").GetDouble(), 0.93, 0.951);
            Assert.InRange(sound.GetProperty("length").GetDouble(), 2.5, 3);
        }
        Assert.Empty(page.Browser.Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_viewer_who_appears_in_first_place_is_a_new_first_place_with_or_without_the_mascot(bool mascot)
    {
        await using var page = await StartAsync(options => options with { Mascot = mascot });
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        var firstCard = (100, (int)((mascot ? Mascot : NoMascot) + CardsY + 49));
        // On an empty board, and then on a board with a viewer in first place that the newcomer outbids.
        foreach (var (uid, price) in new (long, long)[] { (1, 3000), (2, 5000) })
        {
            await page.GiveAsync(uid, price);
            await page.WaitForEventAsync("top");
            // The row is built as a new row is: its place stays empty until the row unfolds there, and it is whole once the cheer is under way.
            await page.SetEventTimeAsync(400);
            Assert.Equal(0, (await page.AlphaAsync([firstCard]))[0]);
            foreach (var time in new[] { 500, 600, 700, 800, 1000, 1100 }) await page.SetEventTimeAsync(time);
            Assert.True((await page.AlphaAsync([firstCard]))[0] > 100);
            await page.SettleAsync();
        }
        Assert.Equal(2, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    [Fact]
    public async Task A_volume_of_zero_loads_no_sound_and_plays_none()
    {
        await using var page = await StartAsync(options => options with { Volume = 0 });
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
    public async Task Ending_the_period_during_a_cheer_stops_it_and_its_sound_at_once_and_leaves_the_page_transparent()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        await page.GiveAsync(1, 3000);
        await page.GiveAsync(2, 5000);
        await page.WaitForEventAsync("top");
        await page.SetEventTimeAsync(300);

        // The clock stands still: the cheer and the one waiting behind it give way to the ended 期 without playing out.
        var received = page.Browser.EventSourceMessages.Count;
        page.H.Client.GiftBoard.EndPeriod();
        await page.WaitForStatesAsync(received + 1);
        await page.Browser.EvaluateAsync(TwoFrames);
        Assert.Equal("", await page.PlayingAsync());
        Assert.All(await page.AlphaAsync(Enumerable.Range(0, 50).Select(step => (240, step * 20))), alpha => Assert.Equal(0, alpha));
        await page.StepClockAsync(5000);
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
        Assert.Empty(page.Browser.Errors);
    }

    [Fact]
    public async Task A_volume_saved_while_a_cheer_plays_or_waits_is_the_one_both_are_heard_at()
    {
        await using var page = await StartAsync();
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.Browser.EvaluateAsync(RecordGains);
        await page.PauseClockAsync();
        await page.GiveAsync(1, 3000);
        await page.WaitForEventAsync("top");
        foreach (var time in new[] { 300, 500, 600, 700, 800, 1000 }) await page.SetEventTimeAsync(time);
        await page.GiveAsync(2, 5000);
        Assert.Equal(1, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());

        // Saving the volume changes nothing on the board, so no state is played for it.
        var received = page.Browser.EventSourceMessages.Count;
        await page.H.SetOptionsAsync(options => options with { Volume = 0 });
        await page.WaitForStatesAsync(received + 1);
        await page.Browser.EvaluateAsync(TwoFrames);
        Assert.Equal(0, (await page.Browser.EvaluateAsync("gains[0].gain.value")).GetDouble());

        // The new first place that was waiting plays without a sound.
        await page.SetEventTimeAsync(3300);
        await page.WaitForEventAsync("top");
        foreach (var time in new[] { 300, 500, 600, 700, 800, 1000, 1500 }) await page.SetEventTimeAsync(time);
        Assert.Equal(1, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    [Fact]
    public async Task A_backlog_of_more_than_three_jumps_to_the_latest_state_and_what_it_skips_is_neither_played_nor_heard()
    {
        await using var page = await StartAsync();
        await page.GiveAsync(1, 3000);
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();
        await page.GiveAsync(2, 2000);
        await page.WaitForEventAsync("new");
        // Behind the playing change wait a new viewer, a new first place, another new viewer, and then a fourth change.
        await page.GiveAsync(3, 1000);
        await page.GiveAsync(2, 2000, 5);
        await page.GiveAsync(4, 500);
        await page.GiveAsync(5, 400);
        await page.StepClockAsync(1700);
        // The first change has played; the four behind it became one redraw of the latest board, without the new first place's sound.
        Assert.Equal("", await page.PlayingAsync());
        Assert.All(await page.AlphaAsync([(100, (int)(Mascot + RowsY + 25))]), alpha => Assert.True(alpha > 100));
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    [Fact]
    public async Task An_ended_period_leaves_the_page_transparent_and_a_continued_one_shows_again()
    {
        await using var page = await StartAsync();
        await page.GiveToAsync(2);
        page.H.Client.GiftBoard.EndPeriod();
        // Down the middle of the page, through the mascot, the ribbon, the cards and the pills.
        var middle = Enumerable.Range(0, 50).Select(step => (240, step * 20)).ToArray();
        await page.OpenAsync(480, 1000);
        await page.Browser.EvaluateAsync(TwoFrames);
        Assert.All(await page.AlphaAsync(middle), alpha => Assert.Equal(0, alpha));

        page.H.Client.GiftBoard.ContinuePeriod();
        await page.WaitForSolidAsync(100, (int)(Mascot + CardsY + 49));
        page.H.Client.GiftBoard.EndPeriod();
        await page.WaitForTransparentAsync(100, (int)(Mascot + CardsY + 49));
        Assert.All(await page.AlphaAsync(middle), alpha => Assert.Equal(0, alpha));
        Assert.Empty(page.Browser.Errors);
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
        await page.H.SetOptionsAsync(options => options with { Mascot = false });
        await page.WaitForSolidAsync(100, (int)(NoMascot + RowsY + 25));
        Assert.Equal("", await page.PlayingAsync());

        // A higher threshold takes rows off the board: another redraw.
        await page.H.SetOptionsAsync(options => options with { Threshold = 49 });
        await page.WaitForTransparentAsync(100, (int)(NoMascot + RowsY + 25));
        Assert.Equal("", await page.PlayingAsync());
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    [Fact]
    public async Task A_threshold_that_puts_a_single_viewer_on_an_empty_board_is_drawn_at_once_without_a_cheer()
    {
        await using var page = await StartAsync(options => options with { Threshold = 100 });
        await page.GiveAsync(1, 5000);
        await page.OpenAsync(480, 1000);
        await page.ClickAsync();
        await page.Browser.EvaluateAsync(RecordSounds);
        await page.PauseClockAsync();

        // The viewer sent 50 电池, under the threshold: lowering it brings one row in first place, but no gift did it.
        await page.H.SetOptionsAsync(options => options with { Threshold = 1 });
        await page.WaitForSolidAsync(100, (int)(Mascot + CardsY + 49));
        Assert.Equal("", await page.PlayingAsync());
        await page.StepClockAsync(2000);
        Assert.Equal(0, (await page.Browser.EvaluateAsync("sounds.length")).GetInt32());
    }

    // Three cards and four pills on a 480 × 640 page, which has room for three pills: the pills scroll.
    private static readonly (long Uid, long Price)[] ScrollingList = [(1, 5000), (2, 4000), (3, 3000), (4, 2000), (5, 1000), (6, 500), (7, 100)];
    private const double ShortScale = 640.0 / 820;
    // Down the middle of the cards' and pills' column (the board is centred, at its scale), and where the cards and the pills start.
    private static readonly int ListColumn = (int)((480 - 480 * ShortScale) / 2 + 100 * ShortScale);
    private static readonly int CardsTop = (int)((Mascot + CardsY) * ShortScale), ListTop = (int)((Mascot + RowsY) * ShortScale);

    private static async Task<PageSession> OpenScrollingListAsync()
    {
        var page = await StartAsync(options => options with { Volume = 0 });
        try
        {
            foreach (var (uid, price) in ScrollingList) await page.GiveAsync(uid, price);
            await page.OpenAsync(480, 640);
            await page.PauseClockAsync();
            return page;
        }
        catch
        {
            await page.DisposeAsync();
            throw;
        }
    }

    private static Task<bool[]> CardsAsync(PageSession page) => page.OpaqueColumnAsync(ListColumn, CardsTop, ListTop - CardsTop);

    private static Task<bool[]> PillsAsync(PageSession page) => page.OpaqueColumnAsync(ListColumn, ListTop, 640 - ListTop);

    [Fact]
    public async Task A_list_too_long_for_the_page_scrolls_from_the_fourth_row_in_a_loop()
    {
        await using var page = await OpenScrollingListAsync();
        var (cards, pills) = (await CardsAsync(page), await PillsAsync(page));

        await page.StepClockAsync(1000);

        Assert.Equal(cards, await CardsAsync(page));
        Assert.NotEqual(pills, await PillsAsync(page));
        // A lap is all four pills and one empty row, 300 design pixels, at the 840 design pixels in 24 seconds that the list scrolls.
        await page.StepClockAsync(8571 - 1000);
        Assert.InRange((await PillsAsync(page)).Zip(pills).Count(pair => pair.First != pair.Second), 0, 2);
    }

    [Fact]
    public async Task The_list_stands_still_while_an_event_plays_and_scrolls_on_when_it_is_over()
    {
        await using var page = await OpenScrollingListAsync();
        // The fifth viewer sends as much again: no one changes places, so this is an event on a pill and nothing else.
        await page.GiveAsync(5, 1000);
        await page.WaitForEventAsync("more");

        await page.SetEventTimeAsync(100);
        var pills = await PillsAsync(page);
        await page.SetEventTimeAsync(600);
        Assert.Equal(pills, await PillsAsync(page));

        await page.SettleAsync();
        await page.StepClockAsync(1000);
        Assert.NotEqual(pills, await PillsAsync(page));
    }

    [Fact]
    public async Task Effects_are_drawn_inside_the_board_and_never_beyond_it()
    {
        await using var page = await StartAsync(options => options with { Volume = 0 });
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
        await using var page = await StartAsync(options => options with { Volume = 0 });
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
        await using var page = await StartAsync(options => options with { Volume = 0 });
        await page.OpenAsync(480, 1000);
        await page.PauseClockAsync();
        var gift = JsonNode.Parse(BlindGiftFixture.Ordinary(uid: 1, nickname: "月下Lily"))!;
        gift["data"]!["face"] = "";
        await page.PushAsync(gift.ToJsonString());
        await page.WaitForEventAsync("top");
        await page.SettleAsync();
        // Left of the avatar's middle the disc is solid; the translucent card on its own never is.
        Assert.Equal(255, (await page.AlphaAsync([(126, (int)(Mascot + CardsY + 49))]))[0]);
    }
}
