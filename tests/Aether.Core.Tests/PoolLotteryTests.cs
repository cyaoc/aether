using Aether.Core.Tests.Support;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Aether.Core.Tests;

public sealed class PoolLotteryTests
{
    private static readonly PoolPrize[] Prizes = [new("抱枕", 1, PrizeGrade.Grand), new("贴纸", 3, PrizeGrade.Third)];

    [Fact]
    public async Task Queued_callback_from_previous_draw_cannot_finish_the_next_draw()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停");
        h.Client.Lottery.Stop();
        h.Time.HoldCallbacksFor = PoolLottery.RevealDuration;
        h.Time.Advance(PoolLottery.RevealDuration);
        await first.DisposeAsync();
        await using var reconnected = await h.WatchConnectedAsync();
        Assert.Equal(PoolLotteryPhase.Idle, h.Client.Lottery.State.Lottery!.Phase);
        h.Client.Lottery.Start(Prizes, "停");
        h.Client.Lottery.Stop();
        h.Time.HoldCallbacksFor = null;
        h.Time.RunHeldCallbacks();
        Assert.Equal(PoolLotteryPhase.Revealing, h.Client.Lottery.State.Lottery.Phase);
        h.Time.Advance(PoolLottery.RevealDuration);
        Assert.Equal(PoolLotteryPhase.Idle, h.Client.Lottery.State.Lottery.Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ended_room_connection_cannot_overwrite_the_next_owners_lottery(bool revealing)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停", 42);
        if (revealing) h.Client.Lottery.Stop();
        await first.DisposeAsync();
        Assert.False(h.Client.Lottery.State.CanControl);
        var nextTime = new RecordingTimeProvider(h.Time.GetUtcNow());
        using var next = new AetherClient(new FakeBilibiliHttp { Respond = h.Http.Respond }, h.Server.ConnectAsync,
            nextTime, h.Logger, h.DataDirectory);
        var second = await h.WatchConnectedAsync(next);
        if (revealing) nextTime.Advance(PoolLottery.RevealDuration);
        else next.Lottery.Cancel();
        next.Lottery.Start([new("新奖品", 1, PrizeGrade.Second)], "开", 99);
        Assert.Throws<InvalidOperationException>(h.Client.Lottery.Stop);
        Assert.Throws<InvalidOperationException>(h.Client.Lottery.Cancel);
        h.Time.Advance(PoolLottery.RevealDuration);
        await second.DisposeAsync();
        await using var refreshed = await h.WatchConnectedAsync(next);
        Assert.Equal(PoolLotteryPhase.Spinning, next.Lottery.State.Lottery!.Phase);
        Assert.Equal("新奖品", Assert.Single(next.Lottery.State.Lottery.Prizes).Name);
        Assert.Equal(99, next.Lottery.State.Lottery.Uid);
    }

    [Fact]
    public async Task Watch_without_GUI_opt_in_does_not_resume_or_run_lotteries()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var gui = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停", 42);
        await gui.DisposeAsync();
        using var cli = h.ClientSharingData(h.Server.ConnectAsync);
        await using (var watch = cli.WatchAsync(6, h.Stop.Token).GetAsyncEnumerator(h.Stop.Token))
        {
            Assert.IsType<Connecting>(await h.NextAsync(watch));
            Assert.IsType<Connected>(await h.NextAsync(watch));
            await h.PushAndCollectAsync(watch, Message(42, "小明", "停"));
            Assert.Null(cli.Lottery.State.Lottery);
        }
        await using var restored = await h.WatchConnectedAsync();
        Assert.Equal(PoolLotteryPhase.Spinning, h.Client.Lottery.State.Lottery!.Phase);
        Assert.Empty(h.Client.Lottery.State.Lottery.Draws);
    }

    [Theory]
    [InlineData(0, "抱枕")]
    [InlineData(1, "贴纸")]
    [InlineData(3, "贴纸")]
    public async Task Weights_host_stop_and_cancel_preserve_the_pool(long pick, string expected)
    {
        await using var h = new WatchHarness(random: new FixedRandom(pick), enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(Prizes, "停");
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        lottery.Start(Prizes, "停", 42);
        lottery.Cancel();
        Assert.Empty(lottery.State.Lottery!.Draws);
        Assert.Throws<InvalidOperationException>(lottery.Stop);
        lottery.Start(Prizes, "停", 42);
        lottery.Stop();
        var draw = Assert.Single(lottery.State.Lottery.Draws);
        Assert.Equal(expected, draw.Prize.Name);
        Assert.Equal("小明", draw.Nickname);
        Assert.Equal(42, draw.Uid);
        Assert.True(draw.StoppedByHost);
        Assert.Throws<InvalidOperationException>(() => lottery.New(Prizes, "停"));
        h.Time.Advance(PoolLottery.RevealDuration);
        lottery.Start(Prizes, "停", 99);
        lottery.Stop();
        Assert.Null(lottery.State.Lottery.Draws[^1].Nickname);
        Assert.Equal(99, lottery.State.Lottery.Draws[^1].Uid);
        h.Time.Advance(PoolLottery.RevealDuration);
        lottery.Start(Prizes, "停");
        await h.PushAndCollectAsync(updates, Message(42, "小明", "停"));
        Assert.Equal(PoolLotteryPhase.Spinning, lottery.State.Lottery.Phase);
        lottery.Stop();
        Assert.Null(lottery.State.Lottery.Draws[^1].Uid);
        Assert.Null(lottery.State.Lottery.Draws[^1].Nickname);
        Assert.Equal(Prizes, lottery.State.Lottery.Prizes);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
        Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Message.Contains("取消"));
        Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Message.Contains("开奖"));
    }

    [Fact]
    public async Task Reconnect_and_restart_restore_active_lottery_draws_viewers_and_reveal_deadline()
    {
        await using var h = new WatchHarness(random: new FixedRandom(0), enableLottery: true);
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start(Prizes, "停", 42);
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        var id = lottery.State.Lottery!.Id;
        lottery.Stop();
        await updates.DisposeAsync();
        Assert.False(lottery.State.Connected);
        Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停"));
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await using (var reconnected = await h.WatchConnectedAsync())
        {
            Assert.Equal(id, lottery.State.Lottery.Id);
            Assert.Single(lottery.State.Lottery.Draws);
            Assert.Equal(PoolLotteryPhase.Revealing, lottery.State.Lottery.Phase);
        }
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var resumed = await h.WatchConnectedAsync(restarted);
        Assert.Equal(id, restarted.Lottery.State.Lottery!.Id);
        Assert.Equal("小明", restarted.Lottery.State.Lottery.Viewers[42]);
        Assert.Equal(PoolLotteryPhase.Revealing, restarted.Lottery.State.Lottery.Phase);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(PoolLotteryPhase.Idle, restarted.Lottery.State.Lottery.Phase);
        restarted.Lottery.Start(Prizes, "停", 42);
        restarted.Lottery.Stop();
        Assert.Equal(2, restarted.Lottery.State.Lottery.Draws.Length);
        Assert.Equal("小明", restarted.Lottery.State.Lottery.Draws[^1].Nickname);
    }

    [Fact]
    public async Task New_lottery_ends_the_previous_one_and_validation_rejects_invalid_input()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        Assert.Throws<ArgumentException>(() => lottery.Start([], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start(Enumerable.Repeat(Prizes[0], 11).ToArray(), "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start([new("", 1, PrizeGrade.Grand)], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start([new("奖品", 0, PrizeGrade.Grand)], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start([new("奖品", 1, (PrizeGrade)10)], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start(Prizes, " "));
        Assert.Throws<ArgumentException>(() => lottery.Start(Prizes, "停", 0));
        lottery.Start(Prizes, "停", 42);
        var first = lottery.State.Lottery!.Id;
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        lottery.New(Prizes, "开");
        Assert.NotEqual(first, lottery.State.Lottery.Id);
        Assert.Empty(lottery.State.Lottery.Draws);
        Assert.Empty(lottery.State.Lottery.Viewers);
        Assert.Equal(PoolLotteryPhase.Idle, lottery.State.Lottery.Phase);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NOT NULL";
        Assert.Equal(1L, query.ExecuteScalar());
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NULL";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Browser_gets_snapshot_changes_and_one_draw_event_and_cannot_control_the_lottery()
    {
        await using var h = new WatchHarness(random: new FixedRandom(1), enableLottery: true);
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        await h.Client.WebService.StartAsync(number);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Address) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        var initial = await Event(reader);
        Assert.Equal("state", initial.Name);
        Assert.False(initial.Data.GetProperty("connected").GetBoolean());
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        Assert.True((await Event(reader)).Data.GetProperty("connected").GetBoolean());
        h.Client.Lottery.Start(Prizes, "停");
        Assert.Equal("Spinning", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        h.Client.Lottery.Stop();
        Assert.Equal("Revealing", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        var draw = await Event(reader);
        Assert.Equal("draw", draw.Name);
        Assert.Equal("贴纸", draw.Data.GetProperty("prize").GetProperty("name").GetString());
        Assert.Equal(10, draw.Data.GetProperty("candidates").GetArrayLength());
        using var reconnected = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reconnectedReader = new StreamReader(await reconnected.Content.ReadAsStreamAsync(h.Stop.Token));
        var snapshot = (await Event(reconnectedReader)).Data.GetProperty("lottery");
        Assert.Equal("Revealing", snapshot.GetProperty("phase").GetString());
        Assert.Equal(draw.Data.GetProperty("id").GetString(), snapshot.GetProperty("draws")[0].GetProperty("id").GetString());
        h.Time.Advance(PoolLottery.RevealDuration);
        Assert.Equal("Idle", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        using var post = await http.PostAsync("lottery/stop", new StringContent(""), h.Stop.Token);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    private static async Task<(string Name, JsonElement Data)> Event(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "";
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ")) name = line[7..];
            if (line.StartsWith("data: ")) return (name, JsonSerializer.Deserialize<JsonElement>(line[6..]));
        }
        throw new EndOfStreamException();
    }

    [Fact]
    public async Task Only_the_named_viewer_can_stop_and_reveal_locks_the_next_draw_for_seven_seconds()
    {
        await using var h = new WatchHarness(random: new FixedRandom(0), enableLottery: true);
        var lottery = h.Client.Lottery;
        Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停", 42));
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        lottery.Start(Prizes, "停", 42);
        Assert.Equal(PoolLotteryPhase.Spinning, lottery.State.Lottery!.Phase);
        await h.PushAndCollectAsync(updates, Message(43, "别人", "停"), Message(42, "小明", "不停"));
        Assert.Empty(lottery.State.Lottery.Draws);
        await h.PushAndCollectAsync(updates, Message(42, "小明", "  停  "));
        var result = Assert.Single(lottery.State.Lottery.Draws);
        Assert.Equal("抱枕", result.Prize.Name);
        Assert.Equal(42, result.Uid);
        Assert.Equal("小明", result.Nickname);
        Assert.False(result.StoppedByHost);
        Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停", 42));
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        Assert.Equal(PoolLotteryPhase.Revealing, lottery.State.Lottery.Phase);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PoolLotteryPhase.Idle, lottery.State.Lottery.Phase);
        lottery.Start(Prizes, "停", 42);
        lottery.Stop();
        Assert.Equal(2, lottery.State.Lottery.Draws.Length);
        Assert.Equal(2, lottery.State.Lottery.Prizes.Length);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    private static string Message(long uid, string name, string text) =>
        System.Text.Json.JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), text, new object[] { uid, name } } });

    private sealed class FixedRandom(long value) : Random
    {
        public override long NextInt64(long maxValue) => value;
    }
}
