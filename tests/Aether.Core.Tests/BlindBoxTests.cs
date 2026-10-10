using System.Text;
using System.Text.Json.Nodes;
using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

public sealed class BlindBoxTests
{
    private static string Gift => BlindGiftFixture.Json;

    [Fact]
    public async Task V1_blind_box_is_saved_with_real_room_integer_amounts_and_original_message()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var raw = Gift;
        await h.PushAndSyncAsync(updates, raw);

        Assert.Equal(new TestDatabase.BlindBoxRow(7734200L, 10001L, "测试观众", 32649L, "星月盲盒", 5000L,
            32698L, "小蛋糕", 1500L, 1L, 5000L, 1500L, 1732634266L, "test-v1-1", raw),
            Assert.Single(TestDatabase.BlindBoxRows(h.DataDirectory)));
        var logged = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("记录盲盒"));
        foreach (var expected in new[] { "测试观众", "星月盲盒", "小蛋糕", "1", "5000", "1500" })
            Assert.Contains(expected, logged.Message);
    }

    [Fact]
    public async Task Disabled_blind_box_setting_skips_records()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift);
        using var connection = TestDatabase.Open(h.DataDirectory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM blind_box";
        Assert.Equal(0L, command.ExecuteScalar());
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("记录盲盒"));
    }

    [Fact]
    public async Task Split_delivery_and_combo_hits_record_each_send_gift_but_not_combo_summaries()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates,
            Gift,
            BlindGiftFixture.With(d => { d["tid"] = "test-v1-2"; d["num"] = 3; d["total_coin"] = 15000; }),
            BlindGiftFixture.With(d => { d["tid"] = "test-v1-3"; d["num"] = 6; d["total_coin"] = 30000; }),
            BlindGiftFixture.With(d => d["tid"] = "test-v1-4"),
            BlindGiftFixture.With(d => d["tid"] = "test-v1-5"),
            Gift.Replace("SEND_GIFT", "COMBO_SEND"), Gift.Replace("SEND_GIFT", "COMBO_END"));

        Assert.Equal(new (long, long, long, string?)[] { (1L, 5000L, 1500L, "test-v1-1"), (3L, 15000L, 4500L, "test-v1-2"),
            (6L, 30000L, 9000L, "test-v1-3"), (1L, 5000L, 1500L, "test-v1-4"), (1L, 5000L, 1500L, "test-v1-5") },
            Amounts(h));
        Assert.Equal(5, h.Logger.Entries.Count(e => e.Level == LogLevel.Information && e.Message.Contains("记录盲盒")));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("missing-blind")]
    [InlineData("silver")]
    [InlineData("anonymous")]
    public async Task Only_gold_blind_boxes_with_known_viewers_are_saved(string kind)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d =>
        {
            if (kind == "ordinary") d["blind_gift"] = null;
            if (kind == "missing-blind") d.Remove("blind_gift");
            if (kind == "silver") d["coin_type"] = "silver";
            if (kind == "anonymous") d["uid"] = 0;
        }));
        Assert.Empty(Amounts(h));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.Contains("记录盲盒"));
        // Never-seen or unverified cases must show at the default Information level, not hide in Debug.
        if (kind == "anonymous")
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("uid 为 0"));
        if (kind == "silver")
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("silver"));
    }

    [Theory]
    [InlineData(14999, 5000)]
    [InlineData(1, long.MaxValue)]
    public async Task Spend_mismatch_keeps_total_coin_and_warns(long spend, long blindPrice)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d =>
        {
            d["num"] = 3; d["total_coin"] = spend; d["blind_gift"]!["original_gift_price"] = blindPrice;
        }));
        Assert.Equal((3L, spend, 4500L, "test-v1-1"), Assert.Single(Amounts(h)));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("total_coin", warning.Message);
        Assert.Contains("test-v1-1", warning.Message);
    }

    [Fact]
    public async Task Duplicate_tid_is_ignored_before_and_after_reconnect_and_on_next_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using (var updates = await h.WatchConnectedAsync())
        {
            await h.PushAndSyncAsync(updates, Gift, Gift);
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.PushAndSyncAsync(updates, Gift);
        }
        using var nextClient = h.ClientSharingData(h.Server.ConnectAsync);
        await using var next = await h.WatchConnectedAsync(nextClient);
        await h.PushAndSyncAsync(next, Gift);

        Assert.Equal((1L, 5000L, 1500L, "test-v1-1"), Assert.Single(Amounts(h)));
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("记录盲盒"));
        var warnings = h.Logger.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("重复推送的礼物，已忽略")).ToArray();
        Assert.Equal(3, warnings.Length);
        Assert.All(warnings, e => Assert.Contains("test-v1-1", e.Message));
    }

    [Fact]
    public async Task Duplicate_tid_warning_keeps_the_ignored_amounts_and_tells_a_repush_from_a_shared_tid()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Gift, Gift, BlindGiftFixture.With(d =>
        {
            d["giftId"] = 32126; d["giftName"] = "棉花糖"; d["price"] = 9000; d["num"] = 2; d["total_coin"] = 10000;
        }));

        Assert.Equal((1L, 5000L, 1500L, "test-v1-1"), Assert.Single(Amounts(h)));
        Assert.Collection(h.Logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message),
            repush =>
            {
                Assert.Contains("重复推送的礼物，已忽略", repush);
                foreach (var expected in new[] { "test-v1-1", "小蛋糕", "5000" }) Assert.Contains(expected, repush);
            },
            shared =>
            {
                Assert.Contains("不是按礼物项唯一", shared);
                foreach (var expected in new[] { "test-v1-1", "棉花糖", "个数 2", "10000", "18000" }) Assert.Contains(expected, shared);
            });
    }

    [Fact]
    public async Task Numeric_tid_is_kept_as_its_digits_instead_of_dropping_the_blind_box()
    {
        // BAC's field table types tid as num although every sample so far is a string.
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d => d["tid"] = JsonNode.Parse("4578879044749173248")));
        Assert.Equal((1L, 5000L, 1500L, "4578879044749173248"), Assert.Single(Amounts(h)));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Blind_box_with_switch_false_is_still_saved_but_warned()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d => d["switch"] = false));
        Assert.Single(Amounts(h));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("switch", warning);
        Assert.Contains("test-v1-1", warning);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("null")]
    public async Task Empty_or_missing_tid_is_null_and_does_not_deduplicate(string kind)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var gift = BlindGiftFixture.With(d =>
        {
            if (kind == "missing") d.Remove("tid");
            else d["tid"] = kind == "empty" ? "" : null;
        });
        await h.PushAndSyncAsync(updates, gift, gift);
        Assert.Equal(new[] { (1L, 5000L, 1500L, (string?)null), (1L, 5000L, 1500L, (string?)null) }, Amounts(h));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("num", "0")]
    [InlineData("num", "-1")]
    [InlineData("num", "9223372036854775807")]
    [InlineData("num", "9223372036854775808")]
    [InlineData("price", "1.5")]
    [InlineData("price", "-1")]
    [InlineData("total_coin", "null")]
    [InlineData("blind_gift", "{}")]
    [InlineData("uname", "null")]
    public async Task Malformed_blind_box_is_warned_without_losing_later_messages(string field, string json)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.With(d => d[field] = JsonNode.Parse(json)), Gift);
        Assert.Single(Amounts(h));
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("跳过无法解析的直播间消息", warning.Message);
        Assert.Contains("SEND_GIFT", warning.Message);
    }

    [Fact]
    public async Task Setting_changes_apply_to_next_room_connection_and_not_automatic_reconnect()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        await using (var updates = await h.WatchConnectedAsync())
        {
            Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "true" });
            await h.PushAndSyncAsync(updates, Gift);
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await h.PushAndSyncAsync(updates, Gift);
            Assert.Empty(Amounts(h));
        }
        await using var next = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(next, Gift);
        Assert.Single(Amounts(h));
    }

    [Fact]
    public async Task Database_write_failure_ends_room_connection_instead_of_silently_losing_the_gift()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        using (var connection = TestDatabase.Open(h.DataDirectory))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER fail_gift BEFORE INSERT ON blind_box BEGIN SELECT RAISE(ABORT, 'test write failure'); END;";
            command.ExecuteNonQuery();
        }
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(Gift), 0));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
            await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.Empty(Amounts(h));
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("直播间连接失败"));
    }

    private static List<(long Num, long Spend, long OpenedValue, string? Tid)> Amounts(WatchHarness h) =>
        TestDatabase.BlindBoxRows(h.DataDirectory).Select(row => (row.Num, row.Spend, row.OpenedValue, row.Tid)).ToList();
}
