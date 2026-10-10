using System.IO.Compression;
using System.Text;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("{", null)]
    [InlineData("{}", null)]
    [InlineData("null", null)]
    [InlineData("[]", null)]
    [InlineData("""{"cmd":null}""", null)]
    [InlineData("""{"cmd":123}""", null)]
    [InlineData("""{"cmd":"DANMU_MSG"}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":{}}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[[],"内容",[]]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[[],"内容",null]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[[],"内容",[0,null]]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[[],"内容",[0,123]]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG","info":[[],null,[0,"观众"]]}""", "DANMU_MSG")]
    [InlineData("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[],{},[0,"观众"]]}""", "DANMU_MSG:4:0:2:2:2:0")]
    public async Task Malformed_room_message_is_warned_and_skipped_while_room_connection_continues(string body, string? command)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Logger.Entries.Clear();
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes(body), 0));
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"正常弹幕",[0,"观众"]]}"""), 0));

        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.Equal(new Danmaku(h.Time.GetLocalNow(), "观众", "正常弹幕"), updates.Current);
        var warning = Assert.Single(EntriesAboveTrace(h));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.Level);
        if (command is not null) Assert.Contains(command, warning.Message);
        Assert.Contains(body, warning.Message);

        var next = updates.MoveNextAsync().AsTask();
        Assert.False(next.IsCompleted);
        await h.Stop.CancelAsync();
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(2048, false)]
    [InlineData(2049, true)]
    public async Task Skipped_message_warning_limits_raw_json_to_2KB_without_splitting_utf8(int byteCount, bool truncated)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Logger.Entries.Clear();
        // 2046 ASCII bytes followed by a 3-byte character straddles the byte limit.
        var body = byteCount == 2048 ? new string('x', 2048) : new string('x', 2046) + "中";
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(body), 0));
        await h.Server.PushAsync(FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes("""{"cmd":"DANMU_MSG","info":[[],"正常",[0,"观众"]]}"""), 0));
        Assert.True(await updates.MoveNextAsync());
        var warning = Assert.Single(EntriesAboveTrace(h)).Message;
        Assert.Contains(truncated ? new string('x', 2046) : body, warning);
        Assert.Equal(truncated, warning.Contains("已截断"));
        Assert.DoesNotContain("中", warning);
        Assert.DoesNotContain("�", warning);
    }

    [Theory]
    [InlineData("truncated-header")]
    [InlineData("zero-length")]
    [InlineData("unsupported-version")]
    [InlineData("invalid-zlib")]
    [InlineData("invalid-brotli")]
    public async Task Invalid_protocol_frames_fail_without_hanging(string kind)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var frame = FakeDanmakuServer.Packet(5, [], kind == "unsupported-version" ? (ushort)4 : (ushort)0);
        if (kind == "truncated-header") frame = frame[..7];
        if (kind == "zero-length") Array.Clear(frame, 0, 4);
        if (kind == "invalid-zlib") frame = FakeDanmakuServer.Packet(5, [255, 255, 255, 255], 2);
        if (kind == "invalid-brotli") frame = FakeDanmakuServer.Packet(5, [255, 255, 255, 255], 3);
        await h.Server.PushAsync(frame);
        var error = await Record.ExceptionAsync(async () =>
            await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.IsType(kind == "invalid-brotli" ? typeof(InvalidOperationException) : typeof(InvalidDataException), error);
        var logged = Assert.Single(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Contains("直播间连接失败", logged.Message);
    }

    [Theory]
    [InlineData("danmaku-brotli.bin", "达***", "[dog]运气抵抗有用吗？")]
    [InlineData("danmaku-zlib.bin", "琴***", "小姐姐没有帮忙抗的吗[dog]")]
    public async Task Captured_frames_produce_expected_danmaku(string fixture, string nickname, string content)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        h.Server.AuthenticationReply = await FixtureAsync("authentication.bin");
        h.Server.FragmentAuthentication = true;
        h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test+8", TimeSpan.FromHours(8), "test+8", "test+8"));
        await using var updates = await h.WatchConnectedAsync();
        h.Time.Advance(TimeSpan.FromSeconds(7));
        await h.Server.PushAsync(await FixtureAsync("heartbeat.bin"));
        await h.Server.PushAsync(await FixtureAsync(fixture));
        Assert.True(await updates.MoveNextAsync());
        var danmaku = Assert.IsType<Danmaku>(updates.Current);
        Assert.Equal(nickname, danmaku.Nickname);
        Assert.Equal(content, danmaku.Content);
        Assert.Equal(new DateTimeOffset(2023, 12, 10, 18, 29, 36, TimeSpan.FromHours(8)), danmaku.ReceivedAt);
        Assert.Equal(TimeSpan.FromHours(8), danmaku.ReceivedAt.Offset);
    }

    [Fact]
    public async Task Captured_multi_packet_frame_logs_each_unknown_command_at_debug_without_updates()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Logger.Entries.Clear();
        await h.Server.PushAsync(await FixtureAsync("multiple-zlib.bin"));
        await h.Server.PushAsync(await FixtureAsync("danmaku-brotli.bin"));
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("达***", Assert.IsType<Danmaku>(updates.Current).Nickname);
        Assert.Collection(EntriesAboveTrace(h),
            entry => { Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, entry.Level); Assert.Contains("ENTRY_EFFECT", entry.Message); },
            entry => { Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, entry.Level); Assert.Contains("ENTRY_EFFECT", entry.Message); });
    }

    [Fact]
    public async Task Gift_message_version_is_reported_when_first_seen_and_again_when_it_switches()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await PushRoomMessagesAsync(h, GiftV1, GiftV1, GiftV2, GiftV2, GiftV1, SyncDanmaku);
        Assert.True(await updates.MoveNextAsync());
        Assert.Collection(GiftVersionNotices(h),
            message => Assert.Contains("V1", message),
            message => Assert.Contains("V2", message),
            message => Assert.Contains("V1", message));
    }

    [Fact]
    public async Task Unknown_danmaku_or_gift_command_is_warned_once_per_command()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await PushRoomMessagesAsync(h, UnknownDanmaku, UnknownDanmaku, """{"cmd":"SEND_GIFT_V3","data":{}}""",
            """{"cmd":"DANMU_MSG_MIRROR","info":[[],"对方直播间",[0,"观众"]]}""", """{"cmd":"INTERACT_WORD_V2","data":{}}""",
            GiftV1, GiftV2, """{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[],"同步",[0,"观众"]]}""");
        Assert.True(await updates.MoveNextAsync());
        Assert.Collection(Warnings(h),
            message => Assert.Contains("DANMU_MSG_V2", message),
            message => Assert.Contains("SEND_GIFT_V3", message));
    }

    [Fact]
    public async Task Format_notices_survive_reconnects_and_restart_with_the_next_room_connection()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using (var updates = await h.WatchConnectedAsync())
        {
            await PushRoomMessagesAsync(h, GiftV1, UnknownDanmaku, SyncDanmaku);
            Assert.True(await updates.MoveNextAsync());
            await h.Server.DisconnectAsync();
            Assert.True(await updates.MoveNextAsync());
            Assert.IsType<Reconnecting>(updates.Current);
            await h.AdvanceRetryAsync(updates, 1);
            Assert.IsType<Connected>(updates.Current);
            await PushRoomMessagesAsync(h, GiftV1, UnknownDanmaku, SyncDanmaku);
            Assert.True(await updates.MoveNextAsync());
            Assert.Single(GiftVersionNotices(h));
            Assert.Single(Warnings(h), message => message.Contains("DANMU_MSG_V2"));
        }

        await using var next = await h.WatchConnectedAsync();
        await PushRoomMessagesAsync(h, GiftV1, UnknownDanmaku, SyncDanmaku);
        Assert.True(await next.MoveNextAsync());
        Assert.Equal(2, GiftVersionNotices(h).Length);
        Assert.Equal(2, Warnings(h).Count(message => message.Contains("DANMU_MSG_V2")));
    }

    private const string UnknownDanmaku ="""{"cmd":"DANMU_MSG_V2","data":{}}""";
    private const string GiftV1 ="""{"cmd":"SEND_GIFT","data":{}}""";
    private const string GiftV2 = """{"cmd":"SEND_GIFT_V2","data":{"pb":""}}""";
    private const string SyncDanmaku = """{"cmd":"DANMU_MSG","info":[[],"同步",[0,"观众"]]}""";

    /// <summary>Pushes room messages in one frame; end with a danmaku and read it to know every earlier one was handled.</summary>
    private static Task PushRoomMessagesAsync(WatchHarness h, params string[] bodies) =>
        h.Server.PushAsync(bodies.SelectMany(body => FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(body), 0)).ToArray());

    private static string[] GiftVersionNotices(WatchHarness h) => h.Logger.Entries
        .Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Message.Contains("礼物消息"))
        .Select(entry => entry.Message).ToArray();

    /// <summary>Everything logged except the Trace captures of raw room messages.</summary>
    private static IEnumerable<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> EntriesAboveTrace(WatchHarness h) =>
        h.Logger.Entries.Where(entry => entry.Level != Microsoft.Extensions.Logging.LogLevel.Trace);

    private static string[] Warnings(WatchHarness h) => h.Logger.Entries
        .Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Select(entry => entry.Message).ToArray();

    private static Task<byte[]> FixtureAsync(string name) =>
        File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Compressed_multi_packet_frames_deliver_every_danmaku_even_across_websocket_fragments(int version)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var packets = new[] { "第一条", "[dog]" }.SelectMany(text => FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes($$"""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[],"{{text}}",[0,"观***"]]}"""), 0)).ToArray();
        var frame = Compress(packets, version);
        var next = updates.MoveNextAsync().AsTask();
        await h.Server.PushAsync(frame[..7], false);
        await h.Server.PushAsync(frame[7..]);
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("第一条", Assert.IsType<Danmaku>(updates.Current).Content);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("[dog]", Assert.IsType<Danmaku>(updates.Current).Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Every_bad_message_in_a_multi_packet_frame_is_warned_without_losing_later_danmaku(int version)
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Logger.Entries.Clear();
        var packets = new[]
        {
            "{",
            """{"cmd":"DANMU_MSG","info":[]}""",
            """{"cmd":"DANMU_MSG","info":[[],"正常弹幕",[0,"观众"]]}"""
        }.SelectMany(body => FakeDanmakuServer.Packet(5, Encoding.UTF8.GetBytes(body), 0)).ToArray();
        if (version != 0) packets = Compress(packets, version);
        await h.Server.PushAsync(packets);

        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), h.Stop.Token));
        Assert.Equal(new Danmaku(h.Time.GetLocalNow(), "观众", "正常弹幕"), updates.Current);
        Assert.Collection(h.Logger.Entries,
            entry => Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level),
            entry => Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level),
            entry => Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Trace, entry.Level));
    }

    /// <summary>Wraps packets in one frame compressed the way protocol version 2 (zlib) or 3 (brotli) does.</summary>
    private static byte[] Compress(byte[] packets, int version)
    {
        using var output = new MemoryStream();
        using (Stream compressor = version == 2
            ? new ZLibStream(output, CompressionLevel.Optimal, true)
            : new BrotliStream(output, CompressionLevel.Optimal, true))
            compressor.Write(packets);
        return FakeDanmakuServer.Packet(5, output.ToArray(), (ushort)version);
    }
}
