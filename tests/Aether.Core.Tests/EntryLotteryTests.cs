using Aether.Core.Tests.Support;
using System.Text.Json;
using static Aether.Core.Tests.Support.ServerSentEvents;

namespace Aether.Core.Tests;

public sealed class EntryLotteryTests
{
    private static readonly LotteryAward[] Awards = [new("一等奖", "键盘", 1, Grade.Grand), new("二等奖", "抱枕", 2, Grade.Second)];

    [Fact]
    public async Task Eligible_entries_run_out_after_each_viewer_has_won()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var controller = h.Client.Lottery;
        await controller.NewAsync(LotteryMode.Entry);
        Assert.False(Assert.IsType<EntryLottery>(controller.State.Lottery).HasEligibleEntries);
        await controller.StartEntryAsync(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, Message(1, "甲", "抽奖"));
        await h.Client.IdleAsync();
        Assert.True(Assert.IsType<EntryLottery>(controller.State.Lottery).HasEligibleEntries);
        await controller.CloseEntriesAsync();
        await controller.DrawEntryAsync(1);
        var lottery = Assert.IsType<EntryLottery>(controller.State.Lottery);
        Assert.False(lottery.HasEligibleEntries);
        var json = JsonSerializer.SerializeToElement<Lottery>(lottery, WebServer.Json);
        Assert.False(json.TryGetProperty("hasEligibleEntries", out _));
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Equal("没有还没中奖的报名观众可抽。", (await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DrawEntryAsync(1))).Message);
    }

    [Fact]
    public async Task The_next_entry_lottery_takes_the_keyword_of_the_rooms_last_one_or_抽奖_even_without_a_room_connection()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        Assert.Equal("抽奖", await h.Client.Lottery.GetNextEntryKeywordAsync());
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, " 参加 ");
        await lottery.CloseEntriesAsync();
        await lottery.EndEntryAsync();
        await lottery.NewAsync(LotteryMode.Pool);

        Assert.Equal("参加", await lottery.GetNextEntryKeywordAsync());
        await updates.DisposeAsync();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        Assert.Equal("参加", await restarted.Lottery.GetNextEntryKeywordAsync());
    }

    [Fact]
    public async Task An_entry_lottery_cannot_open_with_the_helper_keyword()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.ApplySettingsAsync(("desktop_pet.prankster_keyword", "参加"));
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.Lottery.NewAsync(LotteryMode.Entry);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => h.Client.Lottery.StartEntryAsync(Awards, " 参加"));

        Assert.Equal("和桌宠宝箱小游戏里帮它的关键字一样，观众分不清是在报名还是帮它；换一个", error.Message);
        Assert.Equal(EntryLotteryPhase.Preparing, Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Phase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Restart_restores_the_latest_lottery_after_ending_hiding_or_replacing_an_entry_lottery(bool hidden, bool replaced)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Pool);
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, "参加");
        await lottery.CloseEntriesAsync();
        await lottery.EndEntryAsync();
        await lottery.SetEntryHiddenAsync(hidden);
        if (replaced) await lottery.NewAsync(LotteryMode.Pool);
        var current = lottery.State.Lottery!;
        await updates.DisposeAsync();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        await using var resumed = await h.WatchConnectedAsync(restarted);
        await restarted.IdleAsync();
        Assert.Equal(current.Id, restarted.Lottery.State.Lottery!.Id);
        if (replaced)
        {
            var pool = Assert.IsType<PoolLottery>(restarted.Lottery.State.Lottery);
            Assert.Null(pool.EndedAt);
            Assert.False(pool.IsDrawing);
        }
        else
        {
            var entry = Assert.IsType<EntryLottery>(restarted.Lottery.State.Lottery);
            Assert.Equal(EntryLotteryPhase.Ended, entry.Phase);
            Assert.Equal(current.EndedAt, entry.EndedAt);
            Assert.Equal(hidden, entry.Hidden);
            Assert.Equal(Awards, entry.Awards);
        }
    }

    [Fact]
    public async Task Only_exact_trimmed_keywords_during_registration_enter_once_per_uid()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Client.Lottery.StartEntryAsync(Awards, "抽奖"));
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.Lottery.NewAsync(LotteryMode.Entry);
        await h.PushAndCollectAsync(updates, Message(1, "太早", "抽奖"));
        await h.Client.IdleAsync();
        Assert.Empty(Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Entries);
        await h.Client.Lottery.StartEntryAsync(Awards, " 抽奖 ");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "抽奖!"), Message(2, "小红", " 抽奖\n"),
            Message(2, "新昵称", "抽奖"), Message(0, "匿名", "抽奖"), Message(3, "小蓝", "抽 奖"));
        await h.Client.IdleAsync();
        var entry = Assert.Single(Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Entries);
        Assert.Equal(2, entry.Uid);
        Assert.Equal("小红", entry.Nickname);
        Assert.Equal(h.Time.GetUtcNow(), entry.EnteredAt);
        await h.Client.Lottery.CloseEntriesAsync();
        await h.PushAndCollectAsync(updates, Message(3, "太晚", "抽奖"));
        await h.Client.IdleAsync();
        Assert.Single(Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Entries);
        Assert.Equal(EntryLotteryPhase.Closed, Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Phase);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task Draw_is_uniform_excludes_winners_and_locks_for_seven_seconds(long pick, long expectedUid)
    {
        var random = new PickRandom(pick);
        await using var h = new WatchHarness(random: random, enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, Message(1, "甲", "抽奖"), Message(2, "乙", "抽奖"), Message(3, "丙", "抽奖"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool));
        await lottery.CloseEntriesAsync();
        await lottery.DrawEntryAsync(0);
        var draw = Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        Assert.Equal(3, random.Bound);
        Assert.Equal(expectedUid, draw.Winner.Uid);
        Assert.Equal(10, draw.Candidates.Length);
        Assert.All(draw.Candidates, name => Assert.Contains(name, new[] { "甲", "乙", "丙" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(1));
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.EndEntryAsync);
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(1));
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Contains("名额已满", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0))).Message);
        random.Pick = 0;
        await lottery.DrawEntryAsync(1);
        Assert.Equal(2, random.Bound);
        Assert.NotEqual(expectedUid, Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws[^1].Winner.Uid);
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.DrawEntryAsync(1);
        Assert.Equal(1, random.Bound);
        Assert.Equal(3, Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws.Select(d => d.Winner.Uid).Distinct().Count());
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.EndEntryAsync();
        Assert.Equal(EntryLotteryPhase.Ended, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        Assert.False(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
        await lottery.SetEntryHiddenAsync(true);
        Assert.True(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
        await lottery.SetEntryHiddenAsync(false);
        Assert.False(Assert.IsType<EntryLottery>(lottery.State.Lottery).Hidden);
        foreach (var action in new[] { "开始报名", "报名：", "截止", "开奖", "结束" })
            Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains(action));
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_restores_registration_draws_and_ended_visibility(bool ended)
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        await h.Client.Lottery.NewAsync(LotteryMode.Entry);
        await h.Client.Lottery.StartEntryAsync(Awards, "来吧");
        await h.PushAndCollectAsync(updates, Message(7, "七七", "来吧"));
        await h.Client.IdleAsync();
        var id = h.Client.Lottery.State.Lottery!.Id;
        await updates.DisposeAsync();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        updates = await h.WatchConnectedAsync(restarted);
        await restarted.IdleAsync();
        Assert.Equal(id, restarted.Lottery.State.Lottery!.Id);
        Assert.Equal(EntryLotteryPhase.Open, Assert.IsType<EntryLottery>(restarted.Lottery.State.Lottery).Phase);
        Assert.Equal(Awards, Assert.IsType<EntryLottery>(restarted.Lottery.State.Lottery).Awards);
        await h.PushAndCollectAsync(updates, Message(7, "重复", "来吧"));
        await restarted.IdleAsync();
        Assert.Single(Assert.IsType<EntryLottery>(restarted.Lottery.State.Lottery).Entries);
        await restarted.Lottery.CloseEntriesAsync();
        await restarted.Lottery.DrawEntryAsync(0);
        if (ended)
        {
            h.Time.Advance(LotteryController.DrawDuration);
            await restarted.Lottery.EndEntryAsync();
            await restarted.Lottery.SetEntryHiddenAsync(true);
        }
        await updates.DisposeAsync();
        await using var resumed = await h.WatchConnectedAsync();
        await h.Client.IdleAsync();
        var restored = Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery);
        Assert.Equal(id, restored.Id);
        var restoredDraw = Assert.Single(restored.Draws);
        Assert.Equal(Awards[0], restoredDraw.Award);
        Assert.Equal(7, restoredDraw.Winner.Uid);
        Assert.Equal("七七", restoredDraw.Winner.Nickname);
        Assert.Equal(Enumerable.Repeat("七七", 10), restoredDraw.Candidates);
        Assert.Equal(ended, restored.Hidden);
        Assert.Equal(ended ? EntryLotteryPhase.Ended : EntryLotteryPhase.Closed, restored.Phase);
        if (!ended)
        {
            Assert.True(restored.IsDrawing);
            h.Time.Advance(LotteryController.DrawDuration);
            await h.Client.IdleAsync();
            Assert.False(h.Client.Lottery.State.Lottery!.IsDrawing);
        }
        else
        {
            await h.Client.Lottery.SetEntryHiddenAsync(false);
            Assert.False(Assert.IsType<EntryLottery>(h.Client.Lottery.State.Lottery).Hidden);
        }
    }

    [Fact]
    public async Task New_carries_only_the_last_settings_of_the_selected_mode()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        PoolPrize[] pool = [new("手办", 3, Grade.Second)];
        await lottery.StartAsync(pool, "开", 42);
        await h.PushAndCollectAsync(updates, Message(42, "四二", "开"));
        await h.Client.IdleAsync();
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.NewAsync(LotteryMode.Entry);
        Assert.IsType<EntryLottery>(lottery.State.Lottery);
        await lottery.StartEntryAsync(Awards, "参加");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "参加"));
        await lottery.CloseEntriesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool));
        await lottery.DrawEntryAsync(0);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Contains("没有", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(1))).Message);
        await lottery.EndEntryAsync();
        await lottery.NewAsync(LotteryMode.Pool);
        Assert.IsType<PoolLottery>(lottery.State.Lottery);
        Assert.Equal(pool, Assert.IsType<PoolLottery>(lottery.State.Lottery).Prizes);
        Assert.Equal("开", Assert.IsType<PoolLottery>(lottery.State.Lottery).StopWord);
        Assert.Equal(42, Assert.IsType<PoolLottery>(lottery.State.Lottery).Uid);
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Draws);
        Assert.Empty(Assert.IsType<PoolLottery>(lottery.State.Lottery).Viewers);
        await lottery.NewAsync(LotteryMode.Entry);
        Assert.Equal("参加", Assert.IsType<EntryLottery>(lottery.State.Lottery).Keyword);
        Assert.Equal(Awards, Assert.IsType<EntryLottery>(lottery.State.Lottery).Awards);
        Assert.Equal(EntryLotteryPhase.Preparing, Assert.IsType<EntryLottery>(lottery.State.Lottery).Phase);
        Assert.Empty(Assert.IsType<EntryLottery>(lottery.State.Lottery).Entries);
        Assert.Empty(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NULL";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Browser_receives_every_phase_snapshot_recent_entries_draws_and_visibility_in_state_only()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        await Event(reader);
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        Assert.Equal("Preparing", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        await lottery.StartEntryAsync(Awards, "抽奖");
        Assert.Equal("Open", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        for (var uid = 1; uid <= 4; uid++)
        {
            await h.PushAndCollectAsync(updates, Message(uid, $"观众{uid}", "抽奖"));
            var snapshot = (await Event(reader)).Data.GetProperty("lottery");
            Assert.Equal(uid, snapshot.GetProperty("entryCount").GetInt32());
            if (uid == 4)
            {
                Assert.Equal(new long[] { 4, 3, 2 }, snapshot.GetProperty("latestEntries").EnumerateArray().Select(e => e.GetProperty("uid").GetInt64()));
                Assert.False(snapshot.TryGetProperty("entries", out _));
            }
        }
        await lottery.CloseEntriesAsync();
        Assert.Equal("Closed", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        await lottery.DrawEntryAsync(0);
        var drawingEvent = await Event(reader);
        Assert.Equal("state", drawingEvent.Name);
        var drawing = drawingEvent.Data.GetProperty("lottery");
        Assert.Equal("Entry", drawing.GetProperty("mode").GetString());
        Assert.Equal(2, drawing.GetProperty("remaining").GetInt32());
        Assert.Equal("Grand", drawing.GetProperty("awards")[0].GetProperty("grade").GetString());
        var draw = Assert.Single(drawing.GetProperty("draws").EnumerateArray());
        Assert.Equal(10, draw.GetProperty("candidates").GetArrayLength());
        Assert.Equal("键盘", draw.GetProperty("award").GetProperty("prize").GetString());
        using var reconnect = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var again = new StreamReader(await reconnect.Content.ReadAsStreamAsync(h.Stop.Token));
        Assert.Equal(drawing.GetProperty("drawUntil").GetString(), (await Event(again)).Data.GetProperty("lottery").GetProperty("drawUntil").GetString());
        h.Time.Advance(LotteryController.DrawDuration);
        var finished = await Event(reader);
        Assert.Equal("state", finished.Name);
        Assert.Equal(JsonValueKind.Null, finished.Data.GetProperty("lottery").GetProperty("drawUntil").ValueKind);
        await lottery.EndEntryAsync();
        Assert.Equal("Ended", (await Event(reader)).Data.GetProperty("lottery").GetProperty("phase").GetString());
        await lottery.SetEntryHiddenAsync(true);
        Assert.True((await Event(reader)).Data.GetProperty("lottery").GetProperty("hidden").GetBoolean());
        await lottery.SetEntryHiddenAsync(false);
        Assert.False((await Event(reader)).Data.GetProperty("lottery").GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task Invalid_settings_and_illegal_phase_changes_do_not_change_the_lottery()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        var before = lottery.State;
        Assert.Equal(LotteryController.ValidateKeyword(" "), (await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartEntryAsync(Awards, " "))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartEntryAsync([], "抽奖"));
        await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartEntryAsync(Enumerable.Repeat(Awards[0], 6).ToArray(), "抽奖"));
        foreach (var invalid in new[] { Awards[0] with { Name = "" }, Awards[0] with { Prize = " " },
            Awards[0] with { Capacity = 0 }, Awards[0] with { Grade = (Grade)99 } })
            await Assert.ThrowsAsync<ArgumentException>(() => lottery.StartEntryAsync([invalid], "抽奖"));
        Assert.Equal(before, lottery.State);
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.CloseEntriesAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.EndEntryAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.SetEntryHiddenAsync(true));
        await lottery.StartEntryAsync(Awards, "抽奖");
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartEntryAsync(Awards, "改了"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.StartAsync([new("手办", 1, Grade.Grand)], "停"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.NewAsync(LotteryMode.Pool));
        await Assert.ThrowsAsync<InvalidOperationException>(lottery.EndEntryAsync);
        await lottery.CloseEntriesAsync();
        Assert.Contains("没有", (await Assert.ThrowsAsync<InvalidOperationException>(() => lottery.DrawEntryAsync(0))).Message);
        await lottery.EndEntryAsync();
        Assert.Empty(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
    }

    [Fact]
    public async Task Failed_draw_write_is_atomic_and_failed_completion_retries()
    {
        await using var h = new WatchHarness(enableStreamOverlay: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "抽奖"));
        await lottery.CloseEntriesAsync();
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_draw BEFORE INSERT ON lottery_draw BEGIN SELECT RAISE(FAIL, 'unavailable'); END";
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => lottery.DrawEntryAsync(0));
        Assert.Empty(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        Assert.False(lottery.State.Lottery.IsDrawing);
        command.CommandText = "DROP TRIGGER fail_draw";
        command.ExecuteNonQuery();
        await lottery.DrawEntryAsync(0);
        command.CommandText = "CREATE TRIGGER fail_finish BEFORE UPDATE ON lottery BEGIN SELECT RAISE(FAIL, 'unavailable'); END";
        command.ExecuteNonQuery();
        h.Time.Advance(LotteryController.DrawDuration);
        await h.Client.IdleAsync();
        Assert.True(lottery.State.Lottery.IsDrawing);
        command.CommandText = "DROP TRIGGER fail_finish";
        command.ExecuteNonQuery();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await h.Client.IdleAsync();
        Assert.False(lottery.State.Lottery.IsDrawing);
        Assert.Single(Assert.IsType<EntryLottery>(lottery.State.Lottery).Draws);
        await lottery.EndEntryAsync();
    }

    private sealed class PickRandom(long pick) : Random
    {
        public long Pick { get; set; } = pick;
        public long Bound { get; private set; }
        public override long NextInt64(long maxValue) { Bound = maxValue; return Pick; }
    }

    private static string Message(long uid, string name, string text) =>
        System.Text.Json.JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { Array.Empty<object>(), text, new object[] { uid, name } } });
}
