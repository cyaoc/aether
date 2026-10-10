using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using static Aether.Core.Tests.Support.BlindGiftV2Fixture;

namespace Aether.Core.Tests;

public sealed class BlindBoxV2Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ten_boxes_expand_into_three_rows_with_item_amounts_and_original_outer_json(bool nested)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var raw = Message(nested: nested);
        await h.PushAndSyncAsync(updates, raw);

        var rows = TestDatabase.BlindBoxRows(h.DataDirectory);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new TestDatabase.BlindBoxRow(7734200L, 10001L, "测试观众", 35206L, "幸运盲盒", 5000L,
            35311L, "好运柚叶", 2500L, 1L, 5000L, 2500L, 1732634266L, "test-v2-1", raw), rows[0]);
        Assert.Equal(new TestDatabase.BlindBoxRow(7734200L, 10001L, "测试观众", 35206L, "幸运盲盒", 5000L,
            35208L, "星光铃铛", 5200L, 8L, 40000L, 41600L, 1732634266L, "test-v2-2", raw), rows[1]);
        Assert.Equal(new TestDatabase.BlindBoxRow(7734200L, 10001L, "测试观众", 35206L, "幸运盲盒", 5000L,
            35207L, "幸运泡泡", 1500L, 1L, 5000L, 1500L, 1732634266L, "test-v2-3", raw), rows[2]);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(3, h.Logger.Entries.Count(e => e.Message.StartsWith("记录盲盒")));
        Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Trace && e.Message.Contains("SEND_GIFT_V2"));
    }

    [Fact]
    public async Task Sanitized_live_capture_saves_six_blind_box_items_and_ignores_the_ordinary_gift()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var messages = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gifts-v2-20261009.jsonl"));
        await h.PushAndSyncAsync(updates, messages);

        var rows = TestDatabase.BlindBoxRows(h.DataDirectory);
        Assert.Equal(new long[] { 1, 1, 3, 6, 1, 1 }, rows.Select(row => row.Num));
        Assert.Equal(new long[] { 15000, 15000, 45000, 90000, 15000, 15000 }, rows.Select(row => row.Spend));
        Assert.Equal(new long[] { 9000, 2000, 48000, 54000, 9000, 16000 }, rows.Select(row => row.OpenedValue));
        Assert.Equal(new[] { "test-id-03", "test-id-05", "test-id-07", "test-id-09", "test-id-11", "test-id-13" },
            rows.Select(row => row.Tid));
        Assert.Equal(new[] { messages[1], messages[2], messages[2], messages[2], messages[3], messages[4] },
            rows.Select(row => row.RawMessage));
        Assert.All(rows, row => { Assert.Equal(32251L, row.BlindGiftId); Assert.Equal(15000L, row.BlindGiftPrice); });
        // Expected values decoded independently from the same capture (see docs/research/2026-10-09-gift-capture.md).
        Assert.All(rows, row => { Assert.Equal(10001L, row.Uid); Assert.Equal("测试观众", row.Nickname); Assert.Equal("心动盲盒", row.BlindGiftName); });
        Assert.Equal(new long[] { 32126, 32125, 32128, 32126, 32126, 32128 }, rows.Select(row => row.OpenedGiftId));
        Assert.Equal(new[] { "棉花糖", "电影票", "爱心抱枕", "棉花糖", "棉花糖", "爱心抱枕" }, rows.Select(row => row.OpenedGiftName));
        // The send time decides which Beijing day a viewer's 盲盒统计 counts the item in.
        Assert.Equal(new long[] { 1791554681, 1791554956, 1791554956, 1791554956, 1791555128, 1791555129 },
            rows.Select(row => row.Timestamp));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("missing-blind")]
    [InlineData("anonymous")]
    [InlineData("silver")]
    [InlineData("disabled")]
    public async Task V2_uses_the_same_recording_filters_as_V1(string kind)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        if (kind == "disabled") Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["blind_box.enabled"] = "false" });
        await using var updates = await h.WatchConnectedAsync();
        var bytes = kind == "missing-blind" ? Bytes(10, Item())
            : Broadcast([Item(coin: kind == "silver" ? "silver" : "gold")], uid: kind == "anonymous" ? 0 : 10001,
                blindId: kind == "ordinary" ? 0 : 35206);
        await h.PushAndSyncAsync(updates, Message(bytes));
        Assert.Empty(TestDatabase.BlindBoxRows(h.DataDirectory));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Message.StartsWith("记录盲盒"));
        if (kind == "anonymous")
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("uid 为 0"));
        if (kind == "silver")
            Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("silver"));
        else Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task False_or_omitted_switch_keeps_each_item_and_warns(bool? shown)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Message(Broadcast(shown: shown)));
        Assert.Equal(3, TestDatabase.BlindBoxRows(h.DataDirectory).Count);
        var warnings = h.Logger.Entries.Where(e => e.Level == LogLevel.Warning).ToArray();
        Assert.Equal(3, warnings.Length);
        Assert.All(warnings, e => Assert.Contains("switch", e.Message));
    }

    [Fact]
    public async Task Repeated_and_shared_tid_keep_the_first_item_and_warn_with_ignored_amounts()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Message(Broadcast([Item(), Item(), Item(num: 2, spend: 10000)])));
        Assert.Single(TestDatabase.BlindBoxRows(h.DataDirectory));
        Assert.Collection(h.Logger.Entries.Where(e => e.Level == LogLevel.Warning),
            e => Assert.Contains("重复推送的礼物，已忽略", e.Message),
            e =>
            {
                Assert.Contains("不是按礼物项唯一", e.Message);
                Assert.Contains("个数 2", e.Message);
                Assert.Contains("10000", e.Message);
                Assert.Contains("5000", e.Message);
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_or_empty_tid_is_null_and_does_not_deduplicate(string? tid)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Message(Broadcast([Item(tid: tid), Item(tid: tid)])));
        var rows = TestDatabase.BlindBoxRows(h.DataDirectory);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Null(row.Tid));
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Spend_mismatch_keeps_total_coin_and_warns()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Message(Broadcast([Item(spend: 4999)])));
        Assert.Equal(4999L, Assert.Single(TestDatabase.BlindBoxRows(h.DataDirectory)).Spend);
        Assert.Contains("total_coin", Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning).Message);
    }

    [Fact]
    public async Task Each_message_selects_its_version_and_both_versions_share_tid_deduplication()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, BlindGiftFixture.Json, Message(),
            BlindGiftFixture.With(data => data["tid"] = "test-v1-2"), Message(Broadcast([Item(tid: "test-v1-1")])));
        Assert.Equal(5, TestDatabase.BlindBoxRows(h.DataDirectory).Count);
        Assert.Collection(h.Logger.Entries.Where(e => e.Message.Contains("礼物消息为")),
            e => Assert.Contains("V1", e.Message), e => Assert.Contains("V2", e.Message),
            e => Assert.Contains("V1", e.Message), e => Assert.Contains("V2", e.Message));
        Assert.Contains("不是按礼物项唯一", Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning).Message);
    }

    [Fact]
    public async Task Unknown_fields_of_every_wire_type_are_skipped_in_broadcast_blind_gift_and_item()
    {
        // Field 100: varint, fixed64, bytes, nested groups 100/101, fixed32. No production encoder used.
        var unknown = Convert.FromHexString("A0069601A1060102030405060708A2060300FF00A306AB06A00601AC06A406A50601020304");
        byte[] bytes = [.. unknown, .. Broadcast([[.. unknown, .. Item(), .. unknown]]), .. Bytes(9, unknown)];
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, Message(bytes));
        Assert.Equal(2500L, Assert.Single(TestDatabase.BlindBoxRows(h.DataDirectory)).OpenedValue);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("zero-num")]
    [InlineData("negative-num")]
    [InlineData("negative-price")]
    [InlineData("negative-spend")]
    [InlineData("negative-id")]
    [InlineData("negative-time")]
    [InlineData("overflow")]
    // proto3 leaves zero and empty fields out, so a missing send time or name reads as 0 or "" and must not pass.
    [InlineData("missing-time")]
    [InlineData("missing-name")]
    public async Task Invalid_item_rejects_the_whole_message_without_losing_the_next_message(string kind)
    {
        var bad = Item(id: kind == "negative-id" ? -1 : 35311, name: kind == "missing-name" ? "" : "好运柚叶",
            num: kind == "zero-num" ? 0 : kind == "negative-num" ? -1 : kind == "overflow" ? long.MaxValue : 1,
            price: kind == "negative-price" ? -1 : 2500, spend: kind == "negative-spend" ? -1 : 5000,
            timestamp: kind == "negative-time" ? -1 : kind == "missing-time" ? 0 : 1732634266, tid: "bad");
        await AssertMalformedAsync(Message(Broadcast([Item(tid: "must-not-be-saved"), bad])));
    }

    [Theory]
    [InlineData("00")] // Zero tag.
    [InlineData("8080808010")] // Tag beyond uint32.
    [InlineData("08FFFFFFFFFFFFFFFFFF02")] // Varint exceeds 64 bits.
    [InlineData("0880")] // Truncated varint.
    [InlineData("4AFF")] // Truncated length.
    [InlineData("4A08FF")] // Truncated bytes.
    [InlineData("A10600")] // Truncated fixed64.
    [InlineData("A50600")] // Truncated fixed32.
    [InlineData("A306")] // Unclosed group.
    [InlineData("A306AC06")] // Wrong group end.
    [InlineData("A406")] // Unexpected group end.
    [InlineData("A606")] // Invalid wire type 6.
    [InlineData("A706")] // Invalid wire type 7.
    [InlineData("0A00")] // Wrong wire type for uid.
    [InlineData("1201FF")] // Invalid UTF-8 in uname.
    public Task Corrupt_wire_data_warns_and_keeps_the_connection(string hex) => AssertMalformedAsync(Message(Convert.FromHexString(hex)));

    [Theory]
    [InlineData("{\"cmd\":\"SEND_GIFT_V2\",\"data\":{\"pb\":\"!\"}}")]
    [InlineData("{\"cmd\":\"SEND_GIFT_V2\",\"data\":{\"pb\":null}}")]
    [InlineData("{\"cmd\":\"SEND_GIFT_V2\",\"data\":{}}")]
    public Task Missing_or_invalid_base64_warns_and_keeps_the_connection(string raw) => AssertMalformedAsync(raw);

    [Fact]
    public Task Excessive_group_nesting_warns_instead_of_overflowing_the_stack() =>
        AssertMalformedAsync(Message(Enumerable.Repeat(new byte[] { 0xA3, 0x06 }, 65).SelectMany(bytes => bytes).ToArray()));

    private static async Task AssertMalformedAsync(string raw)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.PushAndSyncAsync(updates, raw, Message(Broadcast([Item()])));
        Assert.Equal("test-v2-1", Assert.Single(TestDatabase.BlindBoxRows(h.DataDirectory)).Tid);
        var warning = Assert.Single(h.Logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("跳过无法解析的直播间消息", warning);
        Assert.Contains("SEND_GIFT_V2", warning);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Trace && e.Message.Contains(raw));
    }
}
