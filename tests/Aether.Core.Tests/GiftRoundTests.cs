using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class GiftRoundTests
{
    [Fact]
    public async Task Ordinary_paid_gifts_form_a_round_after_three_seconds_of_silence()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        var gift = BlindGiftFixture.With(d => { d["blind_gift"] = null; d["total_coin"] = 1234; });
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.PushAndSyncAsync(updates, gift);
        h.Time.Advance(TimeSpan.FromMilliseconds(2999));
        await h.PushAndSyncAsync(updates);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        var round = Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        Assert.Equal(2468, round.Spend);
        Assert.Equal(10001, round.Uid);
        Assert.Equal(7734200, round.RoomId);
    }

    [Fact]
    public async Task A_guard_purchase_joins_the_viewers_round_and_counts_once_when_pushed_twice()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d => { d["blind_gift"] = null; d["total_coin"] = 1234; }));
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.PushAndSyncAsync(updates, UserToastFixture.Message(), UserToastFixture.Message(source: 2));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        var round = Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        Assert.Equal((10001L, 199234L), (round.Uid, round.Spend));
    }

    [Fact]
    public async Task A_guard_purchase_by_uid_0_is_only_logged()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, UserToastFixture.Message(uid: 0));
        await h.WaitForLogAsync(LogLevel.Information, "uid 为 0");
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.PushAndSyncAsync(updates);
    }

    [Theory]
    [InlineData("sender_uinfo")]
    [InlineData("guard_info")]
    [InlineData("pay_info")]
    [InlineData("gift_info")]
    [InlineData("option")]
    public async Task A_guard_purchase_missing_a_field_is_warned_without_losing_the_next_one(string field)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, UserToastFixture.Message(edit: data => data.Remove(field)), UserToastFixture.Message());
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(198000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("跳过无法解析的直播间消息", warning);
        Assert.Contains("USER_TOAST_MSG_V2", warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2_paid_items_use_total_coin_and_silver_is_ignored(bool capture)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        var message = capture
            ? File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gifts-v2-20261009.jsonl"))[0]
            : BlindGiftV2Fixture.Message(BlindGiftV2Fixture.Broadcast([
                BlindGiftV2Fixture.Item(num: 3, price: 1000, spend: 2100),
                BlindGiftV2Fixture.Item(coin: "silver", spend: 9999)], blindId: 0));
        await h.PushAndSyncAsync(updates, message);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        var round = Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates));
        Assert.Equal(capture ? 100 : 2100, round.Spend);
    }

    [Fact]
    public async Task Gift_rounds_are_independent_by_viewer_and_do_not_deduplicate_tid()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "false" });
        await using var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json, BlindGiftFixture.Json,
            BlindGiftFixture.With(d => d["uid"] = 20002), BlindGiftFixture.With(d => d["uid"] = 0));
        h.Time.Advance(TimeSpan.FromSeconds(3));
        var rounds = new[] { Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)),
            Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)) };
        Assert.Equal(10000, rounds.Single(r => r.Uid == 10001).Spend);
        Assert.Equal(5000, rounds.Single(r => r.Uid == 20002).Spend);
        await h.PushAndSyncAsync(updates);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(5000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
    }
    [Fact]
    public async Task Reconnect_keeps_the_deadline_but_ending_the_room_discards_the_round()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.announce"] = "false" });
        await using (var updates = await h.WatchConnectedAsync(includeGiftRounds: true))
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Json);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            await h.Server.DisconnectAsync();
            Assert.IsType<Reconnecting>(await h.NextAsync(updates));
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            h.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(5000, Assert.IsType<GiftRoundEnded>(await h.NextAsync(updates)).Spend);
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Json);
        }
        await using var next = await h.WatchConnectedAsync(includeGiftRounds: true);
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.PushAndSyncAsync(next);
    }
}
