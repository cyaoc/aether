using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.GiftBoardStates;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests;

public sealed class GiftBoardRecordingTests
{
    private const string Face = "https://i0.hdslb.com/bfs/face/test-v1.jpg";
    private const string Icon = "https://s1.hdslb.com/bfs/live/test-v1.png";
    private const string FaceInfoUrl = "https://api.live.bilibili.com/live_user/v1/Master/info";
    private const string FetchedFace = "https://i0.hdslb.com/bfs/face/fetched.jpg";

    [Fact]
    public async Task A_gift_sent_while_connected_goes_on_the_board_with_what_the_page_draws()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(num: 2));

        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal(new GiftBoardRow("10001/gift:32698", 10001, "测试观众", Face, "小蛋糕", Icon, 0, 0, 3000, 2), row);
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

    // B站 often leaves the avatar out of a 大航海 purchase.
    [Fact]
    public async Task A_gift_without_an_avatar_keeps_the_latest_one_its_viewer_brought_also_when_read_back()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, UserToastFixture.Message(payflowId: "a"), BlindGiftFixture.Ordinary(), WithoutFace("b"));
            Assert.Equal([Face, Face], (await h.BoardRowsAsync()).Select(row => row.FaceUrl));
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Equal([Face, Face], (await h.BoardRowsAsync()).Select(row => row.FaceUrl));
    }

    [Fact]
    public async Task A_viewer_still_without_an_avatar_gets_bilibilis_after_the_gift_counts_and_again_on_the_next_connection()
    {
        await using var h = new WatchHarness();
        h.Http.Responses[FaceInfoUrl] = System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { info = new { uid = 10001, face = FetchedFace } } });
        await using var states = h.Client.GiftBoard.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            Assert.Empty((await h.NextAsync(states)).Rows);
            await h.PushAndSyncAsync(updates, WithoutFace("a"));

            var read = await h.ReadUntilAsync(states, state => state.Rows is [{ FaceUrl: FetchedFace }]);
            // The gift is counted first; its avatar comes in a state of its own, not as another gift.
            Assert.Equal([("", true), (FetchedFace, false)], read[^2..].Select(state => (state.Rows[0].FaceUrl, state.FromGift)));
            Assert.Contains(h.Http.Requests, request => request.Uri.ToString() == FaceInfoUrl + "?uid=10001");
        }
        await h.ReadUntilAsync(states, state => state.Rows.IsEmpty);

        // Not written down, so a new room connection reads the row back without it and asks again.
        await using var next = await h.WatchConnectedAsync();
        await h.ReadUntilAsync(states, state => state.Rows is [{ FaceUrl: FetchedFace }]);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "")]
    [InlineData(true, null)]
    public async Task An_avatar_bilibili_does_not_give_is_a_warning_and_the_gift_counts_without_one(bool answers, string? face)
    {
        await using var h = new WatchHarness();
        if (answers)
            h.Http.Responses[FaceInfoUrl] = System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { info = new { uid = 10001, face } } });
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, WithoutFace("a"));

        await h.WaitForLogAsync(LogLevel.Warning, "礼物榜读取观众 10001 的头像失败");
        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal(("", 198000L), (row.FaceUrl, row.Value));
    }

    // B站 tells a gift's sender's 大航海身份; a purchase tells only the level bought.
    [Fact]
    public async Task A_purchase_does_not_lower_the_guard_a_gift_told_on_any_of_its_viewers_rows_also_when_read_back()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(guardLevel: 1), UserToastFixture.Message(payflowId: "a"));
            Assert.Equal([(3, 1), (0, 1)], (await h.BoardRowsAsync()).Select(row => (row.GuardLevel, row.ViewerGuardLevel)));
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Equal([(3, 1), (0, 1)], (await h.BoardRowsAsync()).Select(row => (row.GuardLevel, row.ViewerGuardLevel)));
    }

    [Fact]
    public async Task A_purchase_puts_the_level_bought_on_all_its_viewers_rows_until_a_later_gift_tells_the_guard_again()
    {
        await using var h = new WatchHarness();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"), UserToastFixture.Message(payflowId: "b"));
            Assert.Equal([(3, 3), (0, 3)], (await h.BoardRowsAsync()).Select(row => (row.GuardLevel, row.ViewerGuardLevel)));
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Equal([(3, 3), (0, 3)], (await h.BoardRowsAsync()).Select(row => (row.GuardLevel, row.ViewerGuardLevel)));
        // The 舰长 ran out: B站 tells the next gift's sender has none.
        await h.PushAndSyncAsync(next, BlindGiftFixture.Ordinary(tid: "c", guardLevel: 0));
        Assert.Equal([(3, 0), (0, 0)], (await h.BoardRowsAsync()).Select(row => (row.GuardLevel, row.ViewerGuardLevel)));
    }

    private static string WithoutFace(string payflowId) =>
        UserToastFixture.Message(payflowId: payflowId, edit: data => data["sender_uinfo"]!["base"]!.AsObject().Remove("face"));

    [Fact]
    public async Task A_guard_purchase_is_one_row_per_level_priced_by_what_was_paid_for_each_month()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, UserToastFixture.Message(price: 534000, num: 3, payflowId: "a"),
            UserToastFixture.Message(price: 198000, payflowId: "b"), UserToastFixture.Message(guardLevel: 2, price: 1998000, payflowId: "c"));

        // 舰长 cost 534000 for 3 months and 198000 for 1, so 4 months at 183000 each; 提督 ranks above it, and its viewer is a 提督.
        Assert.Equal([
            new GiftBoardRow("10001/guard:2", 10001, "测试观众", UserToastFixture.TestFace, "提督", "", 2, 2, 1998000, 1),
            new GiftBoardRow("10001/guard:3", 10001, "测试观众", UserToastFixture.TestFace, "舰长", "", 3, 2, 732000, 4)], await h.BoardRowsAsync());
    }

    [Fact]
    public async Task Guard_months_bought_at_different_prices_add_up_to_exactly_what_was_paid_also_when_read_back()
    {
        await using var h = new WatchHarness();
        // 舰长 at its list price and at the 续费 price, one month at a time, 7 months before a new room connection reads the board
        // back from the 收礼记录 and 7 after: averaging the price in whole 金瓜子 after each one would come out short.
        string[] Months(int from) => [.. Enumerable.Range(from, 7).Select(i => UserToastFixture.Message(price: i % 2 == 0 ? 198000 : 168000, payflowId: $"p{i}"))];
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, Months(0));
            Assert.Equal((1296000L, 7L), (await h.BoardRowsAsync()) is [var before] ? (before.Value, before.Num) : default);
        }

        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, Months(7));

        var row = Assert.Single(await h.BoardRowsAsync());
        Assert.Equal((2562000L, 14L, 183000L), (row.Value, row.Num, row.Price));
    }

    [Fact]
    public async Task A_gift_from_uid_0_stays_off_the_board_and_is_logged()
    {
        await using var h = new WatchHarness();
        await using var updates = await h.WatchWithGiftBoardAsync();

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(uid: 0), UserToastFixture.Message(uid: 0));

        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal(2, h.Logger.Entries.Count(entry => entry.Level == LogLevel.Information && entry.Message.Contains("uid 为 0 的礼物未记录")));
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
    public async Task A_gift_the_database_cannot_take_stays_on_the_board_and_a_push_again_writes_it_without_counting_twice()
    {
        await using var h = new WatchHarness();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        using var command = connection.CreateCommand();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            connection.Open();
            command.CommandText = "ALTER TABLE gift_record RENAME TO unavailable_record";
            command.ExecuteNonQuery();

            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
            Assert.Contains(h.Logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("礼物榜写数据库失败"));
            Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);

            command.CommandText = "ALTER TABLE unavailable_record RENAME TO gift_record";
            command.ExecuteNonQuery();
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
            Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
            // The database never had it, so this push is no repeat there: it is the one written.
            Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.Contains("重复推送的礼物"));
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
    }

    // A gift belongs to the 期 it was counted in, so a push again that makes up its failed write goes there, not into a 期
    // started in between, where the board never showed it.
    [Fact]
    public async Task A_failed_write_made_up_after_a_new_period_began_goes_into_the_period_the_gift_was_counted_in()
    {
        await using var h = new WatchHarness();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        using var command = connection.CreateCommand();
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            connection.Open();
            command.CommandText = "ALTER TABLE gift_record RENAME TO unavailable_record";
            command.ExecuteNonQuery();
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
            command.CommandText = "ALTER TABLE unavailable_record RENAME TO gift_record";
            command.ExecuteNonQuery();
            h.Client.GiftBoard.EndPeriod(RoomId);
            h.Client.GiftBoard.StartNewPeriod(RoomId);

            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "a"));
            Assert.Empty(await h.BoardRowsAsync());
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal(1L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    // The database writes a tid once, whatever it was written for, so the board counts none it already has: not on the
    // room connection that wrote it, and not on the next.
    [Fact]
    public async Task A_tid_recorded_in_an_earlier_period_or_only_for_the_blind_box_statistics_does_not_count_on_a_new_one()
    {
        await using var h = new WatchHarness();
        var earlier = BlindGiftFixture.Ordinary(tid: "earlier");
        var box = BlindGiftFixture.Gift(new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.FromHours(8)));
        await using (var updates = await h.WatchWithGiftBoardAsync())
        {
            await h.PushAndSyncAsync(updates, earlier);
            h.Client.GiftBoard.EndPeriod(RoomId);
            await h.PushAndSyncAsync(updates, box);
            h.Client.GiftBoard.StartNewPeriod(RoomId);
            await h.PushAndSyncAsync(updates, box);
            Assert.Empty(await h.BoardRowsAsync());
        }

        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, earlier, box);
            Assert.Empty(await h.BoardRowsAsync());
        }

        await using var next = await h.WatchConnectedAsync();
        Assert.Empty(await h.BoardRowsAsync());
        Assert.Equal(2L, TestDatabase.GiftRecords(h.DataDirectory));
    }

    [Fact]
    public async Task A_board_that_cannot_be_read_leaves_danmaku_coming_and_is_read_on_the_next_connection()
    {
        await using var h = new WatchHarness();
        await h.WatchAndLeaveAGiftAsync();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE gift_board_period RENAME TO unavailable_period";
        command.ExecuteNonQuery();

        await using (var updates = await h.WatchConnectedAsync())
        {
            Assert.Contains(h.Logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("礼物榜读数据库失败"));
            Assert.False((await h.Client.GiftBoard.CurrentAsync(h.Stop.Token)).Connected);
            await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(tid: "while-unread"));
        }

        command.CommandText = "ALTER TABLE unavailable_period RENAME TO gift_board_period";
        command.ExecuteNonQuery();
        await using var next = await h.WatchConnectedAsync();
        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
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
    public async Task A_tid_pushed_again_after_a_reconnect_or_on_a_new_room_connection_still_counts_once()
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

        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
    }

    // One record serves both (ADR 0013): the board's 期 coming and going changes nothing in the 盲盒统计, on or off.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_blind_box_statistics_count_the_same_whatever_the_board_does(bool blindBoxes)
    {
        await using var h = new WatchHarness();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = Settings.BooleanText(blindBoxes) });
        var sentAt = new DateTimeOffset(2026, 11, 4, 20, 0, 0, TimeSpan.FromHours(8));
        await using var updates = await h.WatchWithGiftBoardAsync();
        var board = h.Client.GiftBoard;

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Gift(sentAt));
        board.EndPeriod(RoomId);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Gift(sentAt.AddMinutes(1)));
        board.ContinuePeriod(RoomId);
        board.EndPeriod(RoomId);
        board.StartNewPeriod(RoomId);
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Gift(sentAt.AddMinutes(2)));

        // The board has only what its running 期 took: the first and the last went into two different ones.
        Assert.Equal(1, Assert.Single(await h.BoardRowsAsync()).Num);
        var tally = new BlindBoxStore(new Database(h.DataDirectory)).Tally(7734200, 10001, sentAt, BlindBoxPeriod.Daily);
        Assert.Equal(blindBoxes ? new BlindBoxTally(15000, 4500) : null, tally);
    }

    // The same gift, as V1 and as V2 carry it.
    private static string Ordinary(string version, string tid) => version == "v1"
        ? BlindGiftFixture.Ordinary(giftId: 35311, giftName: "好运柚叶", price: 2500, tid: tid)
        : Message(Broadcast([Item(spend: 2500, tid: tid)], blindId: 0));
}
