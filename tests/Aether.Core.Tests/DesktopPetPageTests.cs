using System.Text.Json;
using Aether.Core.Tests.Support;
using static Aether.Core.Tests.Support.GiftBoardStates;

namespace Aether.Core.Tests;

public sealed class DesktopPetPageTests
{
    [Fact]
    public async Task The_page_gets_its_state_first_and_the_web_service_counts_and_logs_its_connections()
    {
        await using var h = new WatchHarness();
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        var status = h.Client.WebService.Status;
        Assert.Equal((0, $"{status.Origin}/desktop-pet"), (status.DesktopPetConnections, status.DesktopPetAddress));

        using var pet = await DesktopPetEvents.OpenAsync(h);

        status = h.Client.WebService.Status;
        Assert.Equal((1, 0, 0, 0), (status.DesktopPetConnections, status.VideoConnections, status.LotteryConnections, status.GiftBoardConnections));
        Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("桌宠页 1 个推流软件"));
        var (name, state) = await pet.NextAsync();
        Assert.Equal("state", name);
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.False(state.GetProperty("roomConnected").GetBoolean());
    }

    [Fact]
    public async Task The_switch_is_off_by_default_and_read_and_checked_like_any_setting()
    {
        await using var h = new WatchHarness();
        Assert.False(Settings.Load(h.DataDirectory).DesktopPetEnabled.Value);
        Assert.Contains("desktop_pet:\n  # true 或 false", File.ReadAllText(Settings.FilePath(h.DataDirectory)).ReplaceLineEndings("\n"));

        File.WriteAllText(Settings.FilePath(h.DataDirectory), "desktop_pet:\n  enabled: true\n");
        Assert.True(Settings.Load(h.DataDirectory).DesktopPetEnabled.Value);
        File.WriteAllText(Settings.FilePath(h.DataDirectory), "desktop_pet:\n  enabled: 开\n");
        var error = Settings.Load(h.DataDirectory).Error;
        Assert.Equal(("desktop_pet.enabled", 2L), (error?.SettingName, error?.LineNumber));
    }

    [Fact]
    public async Task While_the_desktop_pet_is_off_the_page_is_told_only_that()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        await h.ApplySettingsAsync();
        h.EnableStreamOverlay = true;
        await using var updates = await h.WatchConnectedAsync();
        using var pet = await DesktopPetEvents.OpenAsync(h);
        Assert.Equal("""{"enabled":false,"roomConnected":false,"lottery":null,"giftBoard":null}""", (await pet.NextAsync()).Data.GetRawText());

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(), BlindGiftFixture.Json, UserToastFixture.Message(), EntryV2Fixture.Message());
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));

        // Nothing that happened while it was off comes before the state that turns it on.
        var (name, state) = await pet.NextAsync();
        Assert.Equal("state", name);
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.True(state.GetProperty("roomConnected").GetBoolean());
        await h.ApplySettingsAsync(("desktop_pet.enabled", "false"));
        Assert.Equal("""{"enabled":false,"roomConnected":false,"lottery":null,"giftBoard":null}""", (await pet.NextAsync("state")).GetRawText());
    }

    [Fact]
    public async Task Entries_are_told_with_both_welcomes_off_and_a_danmaku_only_as_something_happening()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;

        await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message(uid: 20002, nickname: "进场观众"));
        await h.Server.PushRoomMessageAsync("""{"cmd":"DANMU_MSG","info":[[],"不该上直播画面的话",[30003,"观众"]]}""");

        var (name, entry) = await pet.NextAsync();
        Assert.Equal(("entry", 20002L, "进场观众"), (name, entry.GetProperty("uid").GetInt64(), entry.GetProperty("nickname").GetString()));
        Assert.Equal(("danmaku", "{}"), (await pet.NextAsync()) is var (danmaku, data) ? (danmaku, data.GetRawText()) : default);
    }

    [Fact]
    public async Task The_state_has_the_lottery_as_on_air_with_when_a_draw_is_revealed_but_no_example_nor_what_is_kept_off_air()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;
        var lottery = h.Client.Lottery;
        await lottery.StartAsync([new("贴纸", 1, Grade.Third)], "停");
        await lottery.StopAsync();
        var drawn = (await pet.NextStateAsync(state => state.GetProperty("lottery") is { ValueKind: JsonValueKind.Object } on
            && on.GetProperty("draws").GetArrayLength() == 1)).GetProperty("lottery");
        Assert.Equal(lottery.State.Lottery?.DrawUntil, drawn.GetProperty("drawUntil").GetDateTimeOffset());

        await h.Client.WebService.Lottery.PlayExampleAsync();
        await lottery.HideAllDrawsFromStreamAsync();

        // The example's frames are never told; the change that ends it is, without the draw kept off air.
        var hidden = (await pet.NextAsync("state")).GetProperty("lottery");
        Assert.Equal((drawn.GetProperty("id").GetString(), 0), (hidden.GetProperty("id").GetString(), hidden.GetProperty("draws").GetArrayLength()));
        h.Time.Advance(LotteryController.DrawDuration);
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync([new("一等奖", "贴纸", 1, Grade.Grand)], "参加");
        await lottery.CloseEntriesAsync();
        await lottery.EndEntryAsync();
        await lottery.SetEntryHiddenAsync(true);
        await pet.NextStateAsync(state => state.GetProperty("lottery") is { ValueKind: JsonValueKind.Object } on
            && on.GetProperty("phase").GetString() == "Ended");
        Assert.Equal(JsonValueKind.Null, (await pet.NextAsync("state")).GetProperty("lottery").ValueKind);
    }

    [Fact]
    public async Task The_state_has_the_gift_board_only_while_it_runs()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await using var __ = updates;
        var board = h.Client.GiftBoard;
        await board.SetOptionsAsync(RoomId, GiftBoardOptions.Default with { Threshold = 1 });
        await board.StartNewPeriodAsync(RoomId);
        await pet.NextStateAsync(state => state.GetProperty("giftBoard").ValueKind == JsonValueKind.Object);

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary());
        var gifted = (await pet.NextAsync("state")).GetProperty("giftBoard");
        Assert.Equal((1, true), (gifted.GetProperty("rows").GetArrayLength(), gifted.GetProperty("fromGift").GetBoolean()));
        await board.EndPeriodAsync(RoomId);
        Assert.Equal(JsonValueKind.Null, (await pet.NextAsync("state")).GetProperty("giftBoard").ValueKind);
        await h.SetOptionsAsync(options => options with { Mascot = false });
        await board.StartNewPeriodAsync(RoomId);

        // Nothing is told of the ended board's new options; the next state is the new 期.
        var next = (await pet.NextAsync("state")).GetProperty("giftBoard");
        Assert.Equal((true, false), (next.GetProperty("running").GetBoolean(), next.GetProperty("options").GetProperty("mascot").GetBoolean()));
    }

    [Fact]
    public async Task The_state_tells_when_the_room_connection_starts_and_ends_but_not_a_reconnect()
    {
        await using var h = new WatchHarness();
        var (updates, pet) = await DesktopPetEvents.WatchAsync(h);
        using var _ = pet;
        await h.Server.DisconnectAsync();
        Assert.IsType<Reconnecting>(await h.NextAsync(updates));
        await h.AdvanceRetryAsync(updates, 1);
        Assert.IsType<Connected>(updates.Current);

        await updates.DisposeAsync();

        Assert.False((await pet.NextAsync("state")).GetProperty("roomConnected").GetBoolean());
    }
}
