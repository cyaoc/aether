using System.Net;
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
    public async Task The_state_has_the_settings_and_her_lines_with_the_changed_kinds_and_comes_again_once_saved()
    {
        await using var h = new WatchHarness();
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"), ("desktop_pet.mid_gift_spend", "2000"));
        using var pet = await DesktopPetEvents.OpenAsync(h);
        var state = await pet.NextAsync("state");
        Assert.Equal("""{"off":[],"midGift":200000,"bigGift":1000000,"boredMinutes":15,"gloomyMinutes":40,"sleepyMinutes":60,"dailyMinSeconds":20,"dailyMaxSeconds":40,"mishapPercent":25}""",
            state.GetProperty("settings").GetRawText());
        Assert.Equal(["哇！{昵称}送了{礼物}！", "这么贵重的{礼物}…谢谢{昵称}！", "心跳好快…谢谢{昵称}！", "我会好好珍惜的！"], Lines(state, "bigGift"));

        Settings.SaveDesktopPet(h.DataDirectory, new Dictionary<string, string> { ["desktop_pet.shows_off"] = "[entry]" },
            new Dictionary<string, IReadOnlyList<string>> { ["bigGift"] = ["{昵称}，谢谢你～"] }, Settings.Load(h.DataDirectory));
        await h.Client.WebService.ApplySettingsAsync(Settings.Load(h.DataDirectory));

        var saved = await pet.NextAsync("state");
        Assert.Equal(["entry"], saved.GetProperty("settings").GetProperty("off").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(["{昵称}，谢谢你～"], Lines(saved, "bigGift"));
        Assert.Equal(Character.Current.Lines["gift"], Lines(saved, "gift"));
        Assert.Equal(ShowTable.Current.Lines.Select(kind => kind.Id).Order(), saved.GetProperty("lines").EnumerateObject().Select(kind => kind.Name).Order());

        static string[] Lines(JsonElement state, string kind) => [.. state.GetProperty("lines").GetProperty(kind).EnumerateArray().Select(line => line.GetString()!)];
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
        Assert.Equal("""{"enabled":false,"roomConnected":false,"lottery":null,"giftBoard":null,"settings":null,"lines":null}""", (await pet.NextAsync()).Data.GetRawText());

        await h.PushAndSyncAsync(updates, BlindGiftFixture.Ordinary(), BlindGiftFixture.Json, UserToastFixture.Message(), EntryV2Fixture.Message());
        h.Time.Advance(TimeSpan.FromSeconds(3));
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));

        // Nothing that happened while it was off comes before the state that turns it on.
        var (name, state) = await pet.NextAsync();
        Assert.Equal("state", name);
        Assert.True(state.GetProperty("enabled").GetBoolean());
        Assert.True(state.GetProperty("roomConnected").GetBoolean());
        await h.ApplySettingsAsync(("desktop_pet.enabled", "false"));
        Assert.Equal("""{"enabled":false,"roomConnected":false,"lottery":null,"giftBoard":null,"settings":null,"lines":null}""", (await pet.NextAsync("state")).GetRawText());
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
    public async Task A_test_show_is_refused_with_why_until_a_page_can_play_it_and_can_be_stopped()
    {
        await using var h = new WatchHarness();
        var service = h.Client.WebService;
        async Task<string> RefusedAsync() =>
            (await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestShowAsync("castle", null, "星河"))).Message;

        Assert.Equal("桌宠没有打开。", await RefusedAsync());
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        Assert.Equal("桌宠页没有推流软件连着。", await RefusedAsync());
        await service.StopAsync();
        Assert.Equal(WebService.NotRunning, await RefusedAsync());
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        using var pet = await DesktopPetEvents.OpenAsync(h);
        await pet.NextAsync("state");

        await service.TestShowAsync("blindBoxLarge", "Loss", "星河");
        await service.StopTestShowAsync();

        var (name, show) = await pet.NextAsync();
        Assert.Equal((DesktopPet.Test, "blindBoxLarge", "Loss", "星河"), (name, show.GetProperty("show").GetString(),
            show.GetProperty("variant").GetString(), show.GetProperty("nickname").GetString()));
        Assert.Equal(DesktopPet.StopTest, (await pet.NextAsync()).Name);
    }

    [Fact]
    public async Task The_page_reports_a_test_show_that_is_over_only_from_its_own_origin()
    {
        await using var h = new WatchHarness();
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        await using var updates = h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        Assert.True(await updates.MoveNextAsync());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/desktop-pet/report");
        crossOrigin.Headers.Add("Origin", "https://evil.example");
        using var forbidden = await http.SendAsync(crossOrigin, h.Stop.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var report = await http.PostAsync("/desktop-pet/report", null, h.Stop.Token);

        Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);
        Assert.True(await updates.MoveNextAsync());
        Assert.IsType<DesktopPetTestEnded>(updates.Current);
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
