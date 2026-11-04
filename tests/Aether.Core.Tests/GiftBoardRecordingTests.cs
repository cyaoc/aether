using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests;

public sealed class GiftBoardRecordingTests
{
    private const string Face = "https://i0.hdslb.com/bfs/face/test-v1.jpg";
    private const string Icon = "https://s1.hdslb.com/bfs/live/test-v1.png";

    [Fact]
    public async Task A_gift_sent_while_connected_goes_on_the_board_with_what_the_page_draws()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(num: 2));

        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal(new GiftBoardRow("10001/gift:32698", "测试观众", Face, "小蛋糕", Icon, 0, 1500, 2), row);
    }

    [Fact]
    public async Task The_same_kind_of_gift_from_one_viewer_adds_up_and_keeps_the_latest_nickname()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"), BlindGiftFixture.Ordinary(nickname: "新昵称", num: 2, tid: "b"));

        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal(("新昵称", 3L), (row.Nickname, row.Num));
    }

    [Fact]
    public async Task A_blind_box_counts_as_the_gift_it_opened_and_joins_the_same_gift_sent_directly()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json, BlindGiftFixture.Ordinary(tid: "direct"));

        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal(("10001/gift:32698", "小蛋糕", 1500L, 2L), (row.Id, row.GiftName, row.Price, row.Num));
    }

    [Fact]
    public async Task A_guard_purchase_is_one_row_per_level_priced_by_what_was_paid_for_each_month()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, UserToastFixture.Message(price: 534000, num: 3, payflowId: "a"),
            UserToastFixture.Message(price: 198000, payflowId: "b"), UserToastFixture.Message(guardLevel: 2, price: 1998000, payflowId: "c"));

        // 舰长 cost 534000 for 3 months and 198000 for 1, so 4 months at 183000 each; 提督 ranks above it.
        Assert.Equal([
            new GiftBoardRow("10001/guard:2", "测试观众", UserToastFixture.TestFace, "提督", "", 2, 1998000, 1),
            new GiftBoardRow("10001/guard:3", "测试观众", UserToastFixture.TestFace, "舰长", "", 3, 183000, 4)], await h.BoardRowsAsync());
    }

    [Fact]
    public async Task A_gift_from_uid_0_stays_off_the_board_and_is_logged()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 0), UserToastFixture.Message(uid: 0));

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal(2, h.Logger.Entries.Count(entry => entry.Level == LogLevel.Information && entry.Message.Contains("未上礼物榜")));
    }

    [Theory]
    [InlineData("v1", "v1")]
    [InlineData("v2", "v2")]
    [InlineData("v1", "v2")]
    public async Task A_tid_pushed_twice_counts_once_and_warns(string first, string second)
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, Ordinary(first, "dup"), Ordinary(second, "dup"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        var warning = Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning).Message;
        Assert.Contains("重复推送的礼物，已忽略", warning);
        Assert.Contains("dup", warning);
    }

    [Fact]
    public async Task A_guard_purchase_pushed_twice_with_one_payflow_id_counts_once_and_warns()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, UserToastFixture.Message(payflowId: "pay"), UserToastFixture.Message(payflowId: "pay"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        Assert.Contains("pay", Assert.Single(h.Logger.Entries, entry => entry.Level == LogLevel.Warning).Message);
    }

    [Fact]
    public async Task Gifts_with_different_tids_or_none_all_count()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"), BlindGiftFixture.Ordinary(tid: "b"),
            BlindGiftFixture.Ordinary(tid: null), BlindGiftFixture.Ordinary(tid: null));

        Assert.Equal(4, Assert.Single(await h.BoardRowsAsync()).Num);
        Assert.DoesNotContain(h.Logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_reconnect_keeps_the_seen_tids_but_a_new_room_connection_starts_over()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "same"));
            await h.Server.DisconnectAsync();
            Assert.IsType<Reconnecting>(await h.NextAsync(updates));
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "same"));
            Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        }

        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, BlindGiftFixture.Ordinary(tid: "same"));

        Assert.Equal(2, Assert.Single(await h.BoardRowsAsync()).Num);
    }

    [Fact]
    public async Task Nothing_is_recorded_while_the_board_is_off_and_saving_the_switch_takes_effect_at_once()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "while-off"));

        await h.ApplySettingsAsync(("gift_board.enabled", "true"), ("gift_board.threshold", "1"));
        Assert.Empty(await h.BoardRowsAsync());
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "while-on"));

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
    }

    private static string Ordinary(string version, string tid) => version == "v1"
        ? BlindGiftFixture.Ordinary(tid: tid)
        : Message(Broadcast([Item(tid: tid)], blindId: 0));
}
