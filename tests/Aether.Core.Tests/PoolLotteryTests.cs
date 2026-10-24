using Aether.Core.Tests.Support;
using System.Net;
using System.Text.Json;
using static Aether.Core.Tests.Support.ServerSentEvents;

namespace Aether.Core.Tests;

public sealed class PoolLotteryTests
{
    private static readonly PoolPrize[] Prizes = [new("抱枕", 1, Grade.Grand), new("贴纸", 3, Grade.Third)];

    [Fact]
    public async Task Queued_callback_from_previous_draw_cannot_finish_the_next_draw()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停");
        h.Client.Lottery.Stop();
        h.Time.HoldCallbacksFor = LotteryController.DrawDuration;
        h.Time.Advance(LotteryController.DrawDuration);
        await first.DisposeAsync();
        await using var reconnected = await h.WatchConnectedAsync();
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(h.Client.Lottery.State.Lottery).Phase);
        Assert.False(h.Client.Lottery.State.Lottery!.IsDrawing);
        h.Client.Lottery.Start(Prizes, "停");
        h.Client.Lottery.Stop();
        h.Time.HoldCallbacksFor = null;
        h.Time.RunHeldCallbacks();
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(h.Client.Lottery.State.Lottery).Phase);
        Assert.True(h.Client.Lottery.State.Lottery.IsDrawing);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(h.Client.Lottery.State.Lottery).Phase);
        Assert.False(h.Client.Lottery.State.Lottery.IsDrawing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ended_room_connection_cannot_overwrite_the_next_owners_lottery(bool drawing)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停", 42);
        if (drawing) h.Client.Lottery.Stop();
        await first.DisposeAsync();
        Assert.False(h.Client.Lottery.State.CanControl);
        var nextTime = new RecordingTimeProvider(h.Time.GetUtcNow());
        using var next = new AetherClient(new FakeBilibiliHttp { Respond = h.Http.Respond }, h.Server.ConnectAsync,
            nextTime, h.Logger, h.DataDirectory);
        var second = await h.WatchConnectedAsync(next);
        if (drawing) nextTime.Advance(LotteryController.DrawDuration);
        else next.Lottery.Cancel();
        next.Lottery.Start([new("新奖品", 1, Grade.Second)], "开", 99);
        Assert.Throws<InvalidOperationException>(h.Client.Lottery.Stop);
        Assert.Throws<InvalidOperationException>(h.Client.Lottery.Cancel);
        h.Time.Advance(LotteryController.DrawDuration);
        await second.DisposeAsync();
        await using var refreshed = await h.WatchConnectedAsync(next);
        Assert.Equal(PoolLotteryPhase.Spinning, Assert.IsType<PoolLottery>(next.Lottery.State.Lottery).Phase);
        Assert.Equal("新奖品", Assert.Single(Assert.IsType<PoolLottery>(next.Lottery.State.Lottery).Prizes).Name);
        Assert.Equal(99, Assert.IsType<PoolLottery>(next.Lottery.State.Lottery).Uid);
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
        Assert.Equal(PoolLotteryPhase.Spinning, Assert.IsType<PoolLottery>(h.Client.Lottery.State.Lottery).Phase);
        Assert.Empty(Assert.IsType<PoolLottery>(h.Client.Lottery.State.Lottery).Draws);
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
        lottery.New(LotteryMode.Pool);
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        lottery.Start(Prizes, "停", 42);
        lottery.Cancel();
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.Throws<InvalidOperationException>(lottery.Stop);
        lottery.Start(Prizes, "停", 42);
        lottery.Stop();
        var draw = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.Equal(expected, draw.Prize.Name);
        Assert.Equal("小明", draw.Nickname);
        Assert.Equal(42, draw.Uid);
        Assert.True(draw.StoppedByHost);
        Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool));
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.Start(Prizes, "停", 99);
        lottery.Stop();
        Assert.Null(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws[^1].Nickname);
        Assert.Equal(99, Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws[^1].Uid);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.Start(Prizes, "停");
        await h.PushAndCollectAsync(updates, Message(42, "小明", "停"));
        Assert.Equal(PoolLotteryPhase.Spinning, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        lottery.Stop();
        Assert.Null(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws[^1].Uid);
        Assert.Null(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws[^1].Nickname);
        Assert.Equal(Prizes, Assert.IsType<PoolLottery>(lottery.State.Lottery).Prizes);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
        foreach (var logged in new[] { "新开一场", "开始", "取消", "开奖" })
            Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information && entry.Message.Contains(logged));
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.Start([new("头奖", 1, Grade.Grand), new("贴纸", 99, Grade.Third)], "停");
        lottery.Stop();
        // A prize far below a tenth of the pool still gets its one cell, so a theme can stop on it.
        Assert.Equal(["头奖", .. Enumerable.Repeat("贴纸", 9)], Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws[^1].Candidates.Select(prize => prize.Name));
    }

    [Fact]
    public async Task Reconnect_and_restart_restore_active_lottery_draws_and_draw_deadline_and_only_reconnect_keeps_viewers()
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
            Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
            Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
            Assert.True(lottery.State.Lottery.IsDrawing);
            Assert.Equal("小明", Assert.IsType<PoolLottery>(lottery.State.Lottery).Viewers[42]);
        }
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var resumed = await h.WatchConnectedAsync(restarted);
        Assert.Equal(id, restarted.Lottery.State.Lottery!.Id);
        Assert.Empty(Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Viewers);
        var restoredDraw = Assert.Single(Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Draws);
        Assert.Equal(Prizes[0], restoredDraw.Prize);
        Assert.Equal("小明", restoredDraw.Nickname);
        Assert.Equal([.. Enumerable.Repeat("抱枕", 2), .. Enumerable.Repeat("贴纸", 8)], restoredDraw.Candidates.Select(prize => prize.Name));
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Phase);
        Assert.True(restarted.Lottery.State.Lottery.IsDrawing);
        h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Phase);
        Assert.False(restarted.Lottery.State.Lottery.IsDrawing);
        restarted.Lottery.Start(Prizes, "停", 42);
        restarted.Lottery.Stop();
        Assert.Equal(2, Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Draws.Length);
        // The restart forgot who spoke, so a host stop keeps the UID until the viewer speaks again.
        Assert.Null(Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery).Draws[^1].Nickname);
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
        Assert.Throws<ArgumentException>(() => lottery.Start([new("", 1, Grade.Grand)], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start([new("奖品", 0, Grade.Grand)], "停"));
        Assert.Throws<ArgumentException>(() => lottery.Start([new("奖品", 1, (Grade)10)], "停"));
        Assert.Equal(LotteryController.ValidateStopWord(" "), Assert.Throws<ArgumentException>(() => lottery.Start(Prizes, " ")).Message);
        Assert.Equal(LotteryController.ValidateUid("0"), Assert.Throws<ArgumentException>(() => lottery.Start(Prizes, "停", 0)).Message);
        lottery.Start(Prizes, "停", 42);
        var first = lottery.State.Lottery!.Id;
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        lottery.New(LotteryMode.Pool);
        Assert.NotEqual(first, lottery.State.Lottery.Id);
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Viewers);
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        Assert.False(lottery.State.Lottery.IsDrawing);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NOT NULL";
        Assert.Equal(1L, query.ExecuteScalar());
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NULL";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Browser_gets_snapshot_changes_and_draws_in_state_only_and_cannot_control_the_lottery()
    {
        await using var h = new WatchHarness(random: new FixedRandom(1), enableLottery: true);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
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
        var drawing = await Event(reader);
        Assert.Equal("state", drawing.Name);
        Assert.Equal("Pool", drawing.Data.GetProperty("lottery").GetProperty("mode").GetString());
        Assert.Equal("Idle", drawing.Data.GetProperty("lottery").GetProperty("phase").GetString());
        Assert.NotEqual(JsonValueKind.Null, drawing.Data.GetProperty("lottery").GetProperty("drawUntil").ValueKind);
        var draw = Assert.Single(drawing.Data.GetProperty("lottery").GetProperty("draws").EnumerateArray());
        Assert.Equal("贴纸", draw.GetProperty("prize").GetProperty("name").GetString());
        Assert.Equal([.. Enumerable.Repeat("抱枕", 2), .. Enumerable.Repeat("贴纸", 8)],
            draw.GetProperty("candidates").EnumerateArray().Select(prize => prize.GetProperty("name").GetString()));
        using var reconnected = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reconnectedReader = new StreamReader(await reconnected.Content.ReadAsStreamAsync(h.Stop.Token));
        var snapshot = (await Event(reconnectedReader)).Data.GetProperty("lottery");
        Assert.Equal("Idle", snapshot.GetProperty("phase").GetString());
        Assert.NotEqual(JsonValueKind.Null, snapshot.GetProperty("drawUntil").ValueKind);
        Assert.False(snapshot.TryGetProperty("viewers", out _));
        Assert.Equal(draw.GetProperty("id").GetString(), snapshot.GetProperty("draws")[0].GetProperty("id").GetString());
        h.Time.Advance(LotteryController.DrawDuration);
        var finished = await Event(reader);
        Assert.Equal("state", finished.Name);
        Assert.Equal("Idle", finished.Data.GetProperty("lottery").GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, finished.Data.GetProperty("lottery").GetProperty("drawUntil").ValueKind);
        using var post = await http.PostAsync("lottery/stop", new StringContent(""), h.Stop.Token);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
    }

    [Fact]
    public async Task Browser_clears_once_the_room_connection_ends_and_shows_the_lottery_again_on_reconnect()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        h.Client.Lottery.Start(Prizes, "停");
        await first.DisposeAsync();
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        Assert.Equal(JsonValueKind.Null, (await Event(reader)).Data.GetProperty("lottery").ValueKind);
        await using var second = await h.WatchConnectedAsync();
        Assert.Equal("Spinning", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
    }

    [Fact]
    public async Task Only_the_named_viewer_can_stop_and_a_draw_locks_the_next_draw_for_seven_seconds()
    {
        await using var h = new WatchHarness(random: new FixedRandom(0), enableLottery: true);
        var lottery = h.Client.Lottery;
        Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停", 42));
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        lottery.Start(Prizes, "停", 42);
        Assert.Equal(PoolLotteryPhase.Spinning, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        await h.PushAndCollectAsync(updates, Message(43, "别人", "停"), Message(42, "小明", "不停"));
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        await h.PushAndCollectAsync(updates, Message(42, "小明", "  停  "));
        var result = Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.Equal("抱枕", result.Prize.Name);
        Assert.Equal(42, result.Uid);
        Assert.Equal("小明", result.Nickname);
        Assert.False(result.StoppedByHost);
        Assert.Throws<InvalidOperationException>(() => lottery.Start(Prizes, "停", 42));
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        Assert.True(lottery.State.Lottery.IsDrawing);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        Assert.False(lottery.State.Lottery.IsDrawing);
        lottery.Start(Prizes, "停", 42);
        lottery.Stop();
        Assert.Equal(2, Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws.Length);
        Assert.Equal(2, Assert.IsType<PoolLottery>(lottery.State.Lottery).Prizes.Length);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    [Fact]
    public async Task Failed_expired_draw_recovery_retries_on_the_next_danmaku()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var first = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start(Prizes, "停");
        lottery.Stop();
        await first.DisposeAsync();
        h.Time.Advance(LotteryController.DrawDuration);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_recovery BEFORE UPDATE ON lottery BEGIN SELECT RAISE(FAIL, 'unavailable'); END";
        command.ExecuteNonQuery();
        await using var reconnected = await h.WatchConnectedAsync();
        Assert.False(lottery.State.Connected);
        command.CommandText = "DROP TRIGGER fail_recovery";
        command.ExecuteNonQuery();
        await h.PushAndCollectAsync(reconnected, Message(42, "小明", "你好"));
        Assert.True(lottery.State.Connected);
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        Assert.False(lottery.State.Lottery!.IsDrawing);
        Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        lottery.Start(Prizes, "停");
    }

    [Fact]
    public async Task Failed_reconnect_load_keeps_the_draw_timer_and_retries_on_the_next_danmaku()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start(Prizes, "停", 42);
        lottery.Stop();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE lottery_draw RENAME TO unavailable_draw";
        command.ExecuteNonQuery();
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<Connected>(await h.NextAsync(updates));
        Assert.False(lottery.State.Connected);
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("抽奖读写数据库失败"));
        h.Time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(PoolLotteryPhase.Idle, Assert.IsType<PoolLottery>(lottery.State.Lottery).Phase);
        Assert.False(lottery.State.Lottery!.IsDrawing);
        command.CommandText = "ALTER TABLE unavailable_draw RENAME TO lottery_draw";
        command.ExecuteNonQuery();
        await h.PushAndCollectAsync(updates, Message(42, "小明", "你好"));
        Assert.True(lottery.State.Connected);
        Assert.Single(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        lottery.Start(Prizes, "停", 42);
        await h.PushAndCollectAsync(updates, Message(42, "小明", "停"));
        Assert.Equal(2, Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws.Length);
    }

    [Fact]
    public async Task A_broken_lottery_database_is_logged_and_danmaku_keep_coming()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Client.Lottery.New(LotteryMode.Pool);
        h.Client.Lottery.Start(Prizes, "停", 42);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False"))
        {
            connection.Open();
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE lottery_draw";
            drop.ExecuteNonQuery();
        }
        var arrived = await h.PushAndCollectAsync(updates, Message(42, "小明", "停"));
        Assert.Equal("停", Assert.IsType<Danmaku>(Assert.Single(arrived)).Content);
        Assert.Contains(h.Logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Message.Contains("抽奖读写数据库失败"));
    }

    private static string Message(long uid, string name, string text) =>
        System.Text.Json.JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), text, new object[] { uid, name } } });

    private sealed class FixedRandom(long value) : Random
    {
        public override long NextInt64(long maxValue) => value;
    }
}
