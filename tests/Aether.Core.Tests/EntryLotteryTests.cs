using Aether.Core.Tests.Support;
using System.Text.Json;

namespace Aether.Core.Tests;

public sealed class EntryLotteryTests
{
    private static readonly LotteryAward[] Awards = [new("一等奖", "键盘", 1, PrizeGrade.Grand), new("二等奖", "抱枕", 2, PrizeGrade.Second)];

    [Fact]
    public async Task Only_exact_trimmed_keywords_during_registration_enter_once_per_uid()
    {
        await using var h = new WatchHarness(enableLottery: true);
        Assert.Throws<InvalidOperationException>(() => h.Client.Lottery.StartEntry(Awards, "抽奖"));
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        h.Client.Lottery.New(LotteryMode.Entry);
        await h.PushAndCollectAsync(updates, Message(1, "太早", "抽奖"));
        Assert.Empty(h.Client.Lottery.State.EntryLottery!.Entries);
        h.Client.Lottery.StartEntry(Awards, " 抽奖 ");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "抽奖!"), Message(2, "小红", " 抽奖\n"),
            Message(2, "新昵称", "抽奖"), Message(0, "匿名", "抽奖"), Message(3, "小蓝", "抽 奖"));
        var entry = Assert.Single(h.Client.Lottery.State.EntryLottery!.Entries);
        Assert.Equal(2, entry.Uid);
        Assert.Equal("小红", entry.Nickname);
        Assert.Equal(h.Time.GetUtcNow(), entry.EnteredAt);
        h.Client.Lottery.CloseEntries();
        await h.PushAndCollectAsync(updates, Message(3, "太晚", "抽奖"));
        Assert.Single(h.Client.Lottery.State.EntryLottery.Entries);
        Assert.Equal(EntryLotteryPhase.Closed, h.Client.Lottery.State.EntryLottery.Phase);
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task Draw_is_uniform_excludes_winners_and_locks_for_seven_seconds(long pick, long expectedUid)
    {
        var random = new PickRandom(pick);
        await using var h = new WatchHarness(random: random, enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, Message(1, "甲", "抽奖"), Message(2, "乙", "抽奖"), Message(3, "丙", "抽奖"));
        Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0));
        Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool));
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        var draw = Assert.Single(lottery.State.EntryLottery!.Draws);
        Assert.Equal(3, random.Bound);
        Assert.Equal(expectedUid, draw.Winner.Uid);
        Assert.Equal(10, draw.Candidates.Length);
        Assert.All(draw.Candidates, name => Assert.Contains(name, new[] { "甲", "乙", "丙" }));
        Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(1));
        Assert.Throws<InvalidOperationException>(lottery.EndEntry);
        h.Time.Advance(TimeSpan.FromMilliseconds(6999));
        Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(1));
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Contains("名额已满", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0)).Message);
        random.Pick = 0;
        lottery.DrawEntry(1);
        Assert.Equal(2, random.Bound);
        Assert.NotEqual(expectedUid, lottery.State.EntryLottery.Draws[^1].Winner.Uid);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.DrawEntry(1);
        Assert.Equal(1, random.Bound);
        Assert.Equal(3, lottery.State.EntryLottery.Draws.Select(d => d.Winner.Uid).Distinct().Count());
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.EndEntry();
        Assert.Equal(EntryLotteryPhase.Ended, lottery.State.EntryLottery.Phase);
        Assert.False(lottery.State.EntryLottery.Hidden);
        lottery.SetEntryHidden(true);
        Assert.True(lottery.State.EntryLottery.Hidden);
        lottery.SetEntryHidden(false);
        Assert.False(lottery.State.EntryLottery.Hidden);
        foreach (var action in new[] { "开始报名", "报名：", "截止", "开奖", "结束" })
            Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains(action));
        Assert.Equal(0, SentDanmaku.SendAttempts(h));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_restores_registration_draws_and_ended_visibility(bool ended)
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        h.Client.Lottery.New(LotteryMode.Entry);
        h.Client.Lottery.StartEntry(Awards, "来吧");
        await h.PushAndCollectAsync(updates, Message(7, "七七", "来吧"));
        var id = h.Client.Lottery.State.EntryLottery!.Id;
        await updates.DisposeAsync();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        updates = await h.WatchConnectedAsync(restarted);
        Assert.Equal(id, restarted.Lottery.State.EntryLottery!.Id);
        Assert.Equal(EntryLotteryPhase.Open, restarted.Lottery.State.EntryLottery.Phase);
        Assert.Equal(Awards, restarted.Lottery.State.EntryLottery.Awards);
        await h.PushAndCollectAsync(updates, Message(7, "重复", "来吧"));
        Assert.Single(restarted.Lottery.State.EntryLottery.Entries);
        restarted.Lottery.CloseEntries();
        restarted.Lottery.DrawEntry(0);
        if (ended)
        {
            h.Time.Advance(LotteryController.DrawDuration);
            restarted.Lottery.EndEntry();
            restarted.Lottery.SetEntryHidden(true);
        }
        await updates.DisposeAsync();
        await using var resumed = await h.WatchConnectedAsync();
        var restored = h.Client.Lottery.State.EntryLottery!;
        Assert.Equal(id, restored.Id);
        Assert.Single(restored.Draws);
        Assert.Equal(ended, restored.Hidden);
        Assert.Equal(ended ? EntryLotteryPhase.Ended : EntryLotteryPhase.Closed, restored.Phase);
        if (!ended)
        {
            Assert.NotNull(restored.DrawUntil);
            h.Time.Advance(LotteryController.DrawDuration);
            Assert.Null(h.Client.Lottery.State.EntryLottery!.DrawUntil);
        }
        else
        {
            h.Client.Lottery.SetEntryHidden(false);
            Assert.False(h.Client.Lottery.State.EntryLottery!.Hidden);
        }
    }

    [Fact]
    public async Task New_carries_only_the_last_settings_of_the_selected_mode()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        PoolPrize[] pool = [new("手办", 3, PrizeGrade.Second)];
        lottery.Start(pool, "开", 42);
        await h.PushAndCollectAsync(updates, Message(42, "四二", "开"));
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.New(LotteryMode.Entry);
        Assert.Null(lottery.State.Lottery);
        lottery.StartEntry(Awards, "参加");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "参加"));
        lottery.CloseEntries();
        Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool));
        lottery.DrawEntry(0);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Contains("没有", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(1)).Message);
        lottery.EndEntry();
        lottery.New(LotteryMode.Pool);
        Assert.Null(lottery.State.EntryLottery);
        Assert.Equal(pool, lottery.State.Lottery!.Prizes);
        Assert.Equal("开", lottery.State.Lottery.StopWord);
        Assert.Equal(42, lottery.State.Lottery.Uid);
        Assert.Empty(lottery.State.Lottery.Draws);
        Assert.Empty(lottery.State.Lottery.Viewers);
        lottery.New(LotteryMode.Entry);
        Assert.Equal("参加", lottery.State.EntryLottery!.Keyword);
        Assert.Equal(Awards, lottery.State.EntryLottery.Awards);
        Assert.Equal(EntryLotteryPhase.Preparing, lottery.State.EntryLottery.Phase);
        Assert.Empty(lottery.State.EntryLottery.Entries);
        Assert.Empty(lottery.State.EntryLottery.Draws);
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT count(*) FROM lottery WHERE ended_at IS NULL";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Browser_receives_every_phase_snapshot_recent_entries_draw_and_visibility()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Address) };
        using var response = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token));
        await Event(reader);
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        Assert.Equal("Preparing", (await Event(reader)).Data.GetProperty("entryLottery").GetProperty("phase").GetString());
        lottery.StartEntry(Awards, "抽奖");
        Assert.Equal("Open", (await Event(reader)).Data.GetProperty("entryLottery").GetProperty("phase").GetString());
        for (var uid = 1; uid <= 4; uid++)
        {
            await h.PushAndCollectAsync(updates, Message(uid, $"观众{uid}", "抽奖"));
            var snapshot = (await Event(reader)).Data.GetProperty("entryLottery");
            Assert.Equal(uid, snapshot.GetProperty("entryCount").GetInt32());
            if (uid == 4)
            {
                Assert.Equal(new long[] { 4, 3, 2 }, snapshot.GetProperty("latestEntries").EnumerateArray().Select(e => e.GetProperty("uid").GetInt64()));
                Assert.False(snapshot.TryGetProperty("entries", out _));
            }
        }
        lottery.CloseEntries();
        Assert.Equal("Closed", (await Event(reader)).Data.GetProperty("entryLottery").GetProperty("phase").GetString());
        lottery.DrawEntry(0);
        var drawing = (await Event(reader)).Data.GetProperty("entryLottery");
        Assert.Equal(2, drawing.GetProperty("remaining").GetInt32());
        Assert.Equal("Grand", drawing.GetProperty("awards")[0].GetProperty("grade").GetString());
        var draw = await Event(reader);
        Assert.Equal("draw", draw.Name);
        Assert.Equal(10, draw.Data.GetProperty("candidates").GetArrayLength());
        Assert.Equal("键盘", draw.Data.GetProperty("award").GetProperty("prize").GetString());
        using var reconnect = await http.GetAsync("lottery/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        using var again = new StreamReader(await reconnect.Content.ReadAsStreamAsync(h.Stop.Token));
        Assert.Equal(drawing.GetProperty("drawUntil").GetString(), (await Event(again)).Data.GetProperty("entryLottery").GetProperty("drawUntil").GetString());
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.Equal(JsonValueKind.Null, (await Event(reader)).Data.GetProperty("entryLottery").GetProperty("drawUntil").ValueKind);
        lottery.EndEntry();
        Assert.Equal("Ended", (await Event(reader)).Data.GetProperty("entryLottery").GetProperty("phase").GetString());
        lottery.SetEntryHidden(true);
        Assert.True((await Event(reader)).Data.GetProperty("entryLottery").GetProperty("hidden").GetBoolean());
        lottery.SetEntryHidden(false);
        Assert.False((await Event(reader)).Data.GetProperty("entryLottery").GetProperty("hidden").GetBoolean());
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
    public async Task Invalid_settings_and_illegal_phase_changes_do_not_change_the_lottery()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        var before = lottery.State;
        Assert.Throws<ArgumentException>(() => lottery.StartEntry(Awards, " "));
        Assert.Throws<ArgumentException>(() => lottery.StartEntry([], "抽奖"));
        Assert.Throws<ArgumentException>(() => lottery.StartEntry(Enumerable.Repeat(Awards[0], 6).ToArray(), "抽奖"));
        foreach (var invalid in new[] { Awards[0] with { Name = "" }, Awards[0] with { Prize = " " },
            Awards[0] with { Capacity = 0 }, Awards[0] with { Grade = (PrizeGrade)99 } })
            Assert.Throws<ArgumentException>(() => lottery.StartEntry([invalid], "抽奖"));
        Assert.Equal(before, lottery.State);
        Assert.Throws<InvalidOperationException>(lottery.CloseEntries);
        Assert.Throws<InvalidOperationException>(lottery.EndEntry);
        Assert.Throws<InvalidOperationException>(() => lottery.SetEntryHidden(true));
        lottery.StartEntry(Awards, "抽奖");
        Assert.Throws<InvalidOperationException>(() => lottery.StartEntry(Awards, "改了"));
        Assert.Throws<InvalidOperationException>(() => lottery.Start([new("手办", 1, PrizeGrade.Grand)], "停"));
        Assert.Throws<InvalidOperationException>(() => lottery.New(LotteryMode.Pool));
        Assert.Throws<InvalidOperationException>(lottery.EndEntry);
        lottery.CloseEntries();
        Assert.Contains("没有", Assert.Throws<InvalidOperationException>(() => lottery.DrawEntry(0)).Message);
        lottery.EndEntry();
        Assert.Empty(lottery.State.EntryLottery!.Draws);
    }

    [Fact]
    public async Task Failed_draw_write_is_atomic_and_failed_completion_retries()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry(Awards, "抽奖");
        await h.PushAndCollectAsync(updates, Message(1, "小明", "抽奖"));
        lottery.CloseEntries();
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(h.DataDirectory, "aether.db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TRIGGER fail_draw BEFORE INSERT ON lottery_draw BEGIN SELECT RAISE(FAIL, 'unavailable'); END";
        command.ExecuteNonQuery();
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => lottery.DrawEntry(0));
        Assert.Empty(lottery.State.EntryLottery!.Draws);
        Assert.Null(lottery.State.EntryLottery.DrawUntil);
        command.CommandText = "DROP TRIGGER fail_draw";
        command.ExecuteNonQuery();
        lottery.DrawEntry(0);
        command.CommandText = "CREATE TRIGGER fail_finish BEFORE UPDATE ON lottery BEGIN SELECT RAISE(FAIL, 'unavailable'); END";
        command.ExecuteNonQuery();
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.NotNull(lottery.State.EntryLottery.DrawUntil);
        command.CommandText = "DROP TRIGGER fail_finish";
        command.ExecuteNonQuery();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(lottery.State.EntryLottery.DrawUntil);
        Assert.Single(lottery.State.EntryLottery.Draws);
        lottery.EndEntry();
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
