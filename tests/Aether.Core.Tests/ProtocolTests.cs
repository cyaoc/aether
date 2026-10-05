using System.IO.Compression;
using System.Text;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("truncated-header")]
    [InlineData("zero-length")]
    [InlineData("unsupported-version")]
    public async Task Invalid_protocol_frames_fail_without_hanging(string kind)
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var frame = FakeDanmakuServer.Packet(5, [], kind == "unsupported-version" ? (ushort)4 : (ushort)0);
        if (kind == "truncated-header") frame = frame[..7];
        if (kind == "zero-length") Array.Clear(frame, 0, 4);
        await h.Server.PushAsync(frame);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("danmaku-brotli.bin", "达***", "[dog]运气抵抗有用吗？")]
    [InlineData("danmaku-zlib.bin", "琴***", "小姐姐没有帮忙抗的吗[dog]")]
    public async Task Captured_frames_produce_expected_danmaku(string fixture, string nickname, string content)
    {
        await using var h = new WatchHarness();
        h.Server.AuthenticationReply = await FixtureAsync("authentication.bin");
        h.Server.FragmentAuthentication = true;
        h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test+8", TimeSpan.FromHours(8), "test+8", "test+8"));
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connecting>(updates.Current);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<Connected>(updates.Current);
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
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        await h.Server.PushAsync(await FixtureAsync("multiple-zlib.bin"));
        await h.Server.PushAsync(await FixtureAsync("danmaku-brotli.bin"));
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("达***", Assert.IsType<Danmaku>(updates.Current).Nickname);
        Assert.Collection(h.Logger.Entries,
            entry => { Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, entry.Level); Assert.Contains("ENTRY_EFFECT", entry.Message); },
            entry => { Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, entry.Level); Assert.Contains("ENTRY_EFFECT", entry.Message); });
    }

    private static Task<byte[]> FixtureAsync(string name) =>
        File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Compressed_multi_packet_frames_deliver_every_danmaku_even_across_websocket_fragments(int version)
    {
        await using var h = new WatchHarness();
        await using var updates = h.Client.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        await updates.MoveNextAsync();
        await updates.MoveNextAsync();
        var packets = new[] { "第一条", "[dog]" }.SelectMany(text => FakeDanmakuServer.Packet(5,
            Encoding.UTF8.GetBytes($$"""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[[],"{{text}}",[0,"观***"]]}"""), 0)).ToArray();
        using var output = new MemoryStream();
        using (Stream compressor = version == 2
            ? new ZLibStream(output, CompressionLevel.Optimal, true)
            : new BrotliStream(output, CompressionLevel.Optimal, true))
            compressor.Write(packets);
        var frame = FakeDanmakuServer.Packet(5, output.ToArray(), (ushort)version);
        var next = updates.MoveNextAsync().AsTask();
        await h.Server.PushAsync(frame[..7], false);
        await h.Server.PushAsync(frame[7..]);
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("第一条", Assert.IsType<Danmaku>(updates.Current).Content);
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("[dog]", Assert.IsType<Danmaku>(updates.Current).Content);
    }
}
