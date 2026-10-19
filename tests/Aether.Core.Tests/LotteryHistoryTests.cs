using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class LotteryHistoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-19T08:04:05.7654321-05:30")]
    public async Task History_preserves_saved_timestamp_offsets_and_nullable_fields(string? endedAt)
    {
        await using var h = new WatchHarness();
        Assert.Empty(h.Client.Lottery.GetHistory(7734200));
        using (var connection = TestDatabase.Open(h.DataDirectory))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO lottery (id, room_id, mode, stop_word, started_at, ended_at, phase, uid, draw_until)
                VALUES ('saved', 7734200, 'pool', '停', '2026-10-19T08:01:02.1234567+08:00', $ended, 0, NULL, NULL);
                """;
            command.Parameters.AddWithValue("$ended", (object?)endedAt ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        var history = Assert.Single(h.Client.Lottery.GetHistory(7734200));
        var details = Assert.IsType<PoolLottery>(h.Client.Lottery.GetHistoryDetails(7734200, "saved"));
        Assert.Equal("2026-10-19T08:01:02.1234567+08:00", history.StartedAt.ToString("O"));
        Assert.Equal("2026-10-19T08:01:02.1234567+08:00", details.StartedAt.ToString("O"));
        Assert.Equal(endedAt, history.EndedAt?.ToString("O"));
        Assert.Equal(endedAt, details.EndedAt?.ToString("O"));
        Assert.Null(details.Uid);
        Assert.Null(details.DrawUntil);
    }

    [Fact]
    public async Task Pool_history_details_return_the_ended_lottery_with_the_complete_draw()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start([new("键盘", 1, Grade.Grand), new("贴纸", 3, Grade.Third)], "开", 42);
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"开",[42,"小明"]]}""");
        h.Time.Advance(LotteryController.DrawDuration);
        var original = Assert.IsType<PoolLottery>(lottery.State.Lottery);
        var endedAt = h.Time.GetUtcNow();
        lottery.New(LotteryMode.Entry);

        var details = Assert.IsType<PoolLottery>(lottery.GetHistoryDetails(7734200, original.Id));
        Assert.Equal(original.Id, details.Id);
        Assert.Equal(original.RoomId, details.RoomId);
        Assert.Equal(original.StartedAt, details.StartedAt);
        Assert.Equal(endedAt, details.EndedAt);
        Assert.Equal(original.StopWord, details.StopWord);
        Assert.Equal(original.Uid, details.Uid);
        Assert.Equal(original.Prizes, details.Prizes);
        Assert.Equal(PoolLotteryPhase.Idle, details.Phase);
        Assert.False(details.IsDrawing);
        Assert.Empty(details.Viewers);
        Assert.Equivalent(Assert.Single(original.Draws), Assert.Single(details.Draws));
    }

    [Fact]
    public async Task Entry_history_details_return_all_registration_and_draw_fields()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("幸运奖", "抱枕", 2, Grade.Second)], "参加");
        await h.PushAndCollectAsync(updates,
            """{"cmd":"DANMU_MSG","info":[[],"参加",[7,"七七"]]}""",
            """{"cmd":"DANMU_MSG","info":[[],"参加",[42,"小明"]]}""");
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.EndEntry();
        lottery.SetEntryHidden(true);
        var original = Assert.IsType<EntryLottery>(lottery.State.Lottery);
        lottery.New(LotteryMode.Pool);

        var details = Assert.IsType<EntryLottery>(lottery.GetHistoryDetails(7734200, original.Id));
        Assert.Equivalent(original, details);
    }

    [Fact]
    public async Task History_orders_by_time_then_creation_and_marks_replaced_preparation_as_ended()
    {
        await using var h = new WatchHarness(enableLottery: true);
        await h.LoginAsync();
        await using var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.New(LotteryMode.Entry);
        var first = lottery.State.Lottery!.Id;
        lottery.New(LotteryMode.Pool);
        var second = lottery.State.Lottery!.Id;
        lottery.New(LotteryMode.Entry);
        var third = lottery.State.Lottery!.Id;
        var history = lottery.GetHistory(7734200);
        Assert.Equal(new[] { third, second, first }, history.Select(item => item.Id));
        Assert.Null(history[0].EndedAt);
        Assert.All(history.Skip(1), item => Assert.NotNull(item.EndedAt));
        Assert.All(history, item => Assert.Equal(0, item.DrawCount));
    }

    [Fact]
    public async Task History_includes_both_modes_and_unfinished_lotteries_after_restart()
    {
        await using var h = new WatchHarness(enableLottery: true);
        Assert.Empty(h.Client.Lottery.GetHistory(7734200));
        await h.LoginAsync();
        var updates = await h.WatchConnectedAsync();
        var lottery = h.Client.Lottery;
        lottery.Start([new("键盘", 1, Grade.Grand)], "开", 42);
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"开",[42,"小明"]]}""");
        var poolId = lottery.State.Lottery!.Id;
        Assert.Null(Assert.Single(lottery.GetHistory(7734200)).EndedAt);
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.New(LotteryMode.Entry);
        lottery.StartEntry([new("幸运奖", "抱枕", 2, Grade.Second)], "参加");
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"参加",[7,"七七"]]}""");
        lottery.CloseEntries();
        lottery.DrawEntry(0);
        var entryId = lottery.State.Lottery!.Id;
        h.Time.Advance(LotteryController.DrawDuration);
        lottery.EndEntry();
        lottery.New(LotteryMode.Pool);
        var currentId = lottery.State.Lottery!.Id;
        await updates.DisposeAsync();
        h.Client.Dispose();
        using var restarted = h.ClientSharingData(h.Server.ConnectAsync);
        var history = restarted.Lottery.GetHistory(7734200);
        Assert.Equal(new[] { currentId, entryId, poolId }, history.Select(item => item.Id));
        Assert.Null(history[0].EndedAt);
        Assert.All(history.Skip(1), item => Assert.NotNull(item.EndedAt));
        Assert.Equal(LotteryMode.Entry, history[1].Mode);
        Assert.Equal(1, history[1].EntryCount);
        Assert.Equal(1, history[1].DrawCount);
        Assert.Equal("幸运奖", Assert.Single(history[1].Awards).Name);
        Assert.Equal("键盘", Assert.Single(history[2].Prizes).Name);
        var entry = Assert.IsType<EntryLottery>(restarted.Lottery.GetHistoryDetails(7734200, entryId));
        Assert.Equal("参加", entry.Keyword);
        var entryDraw = Assert.Single(entry.Draws);
        Assert.Equal("七七", entryDraw.Winner.Nickname);
        Assert.Equal("抱枕", entryDraw.Award.Prize);
        var pool = Assert.IsType<PoolLottery>(restarted.Lottery.GetHistoryDetails(7734200, poolId));
        Assert.Equal("开", pool.StopWord);
        var poolDraw = Assert.Single(pool.Draws);
        Assert.Equal(42, poolDraw.Uid);
        Assert.Equal("小明", poolDraw.Nickname);
        Assert.Equal("键盘", poolDraw.Prize.Name);
        Assert.Empty(restarted.Lottery.GetHistory(999));
        Assert.Null(restarted.Lottery.GetHistoryDetails(999, poolId));
        Assert.Null(restarted.Lottery.GetHistoryDetails(7734200, "missing"));
        Assert.Null(restarted.Lottery.State.RoomId);
    }
}
