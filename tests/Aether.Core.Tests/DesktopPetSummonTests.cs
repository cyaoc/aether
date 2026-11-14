using System.Net;
using System.Text.Json;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

/// <summary>叫它来 (#375): Core decides whether the streamer can, with why not, and where the 捣蛋鬼 is as the 桌宠页 reports it,
/// and tells the GUI each time any of it changes.</summary>
public sealed class DesktopPetSummonTests : IAsyncDisposable
{
    private const string Busy = "抽完奖才能叫：抽奖会把它吓跑";
    private readonly WatchHarness h = new();
    private IAsyncEnumerator<PranksterSummon>? summons;
    private Task<bool>? next;
    private PranksterSummon? last;

    [Fact]
    public async Task Why_it_cannot_come_is_told_as_it_changes_without_a_press()
    {
        Assert.Equal(new PranksterSummon("先打开桌宠", PranksterVisit.Away), await NextAsync());
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        Assert.Equal("先在直播姬里加好桌宠页", (await NextAsync()).Blocked);
        using var pet = await DesktopPetEvents.OpenAsync(h);
        Assert.Equal("连上直播间才能叫它来：没人送礼，也没人能帮它", (await NextAsync()).Blocked);
        await h.LoginAsync();
        h.EnableStreamOverlay = true;
        await using var updates = await h.WatchConnectedAsync();

        Assert.True((await NextAsync()).CanSummon);
        await h.Client.WebService.StopAsync();
        Assert.Equal("先在直播姬里加好桌宠页", (await NextAsync()).Blocked);
    }

    [Fact]
    public async Task A_lottery_keeps_it_away_while_entries_are_open_or_closed_with_someone_or_a_pool_spins_or_draws_but_not_between_draws()
    {
        var (updates, pet) = await ConnectedAsync();
        using var _ = pet;
        await using var __ = updates;
        var lottery = h.Client.Lottery;
        LotteryAward[] awards = [new("一等奖", "贴纸", 1, Grade.Grand)];

        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(awards, "参加");
        Assert.Equal(Busy, (await NextAsync()).Blocked);
        await h.PushAndCollectAsync(updates, """{"cmd":"DANMU_MSG","info":[[],"参加",[30003,"观众"]]}""");
        await lottery.CloseEntriesAsync();
        await OnAirAsync(pet, on => on.GetProperty("phase").GetString() == "Closed");
        Assert.Equal(Busy, (await LatestAsync()).Blocked);
        await lottery.EndEntryAsync();
        Assert.True((await NextAsync()).CanSummon);
        await lottery.NewAsync(LotteryMode.Entry);
        await lottery.StartEntryAsync(awards, "参加");
        Assert.Equal(Busy, (await NextAsync()).Blocked);
        await lottery.CloseEntriesAsync();
        Assert.True((await NextAsync()).CanSummon);

        await lottery.EndEntryAsync();
        await lottery.NewAsync(LotteryMode.Pool);
        await lottery.StartAsync([new("贴纸", 1, Grade.Third)], "停");
        Assert.Equal(Busy, (await NextAsync()).Blocked);
        await lottery.StopAsync();
        await OnAirAsync(pet, on => on.GetProperty("drawUntil").ValueKind == JsonValueKind.String);
        Assert.Equal(Busy, (await LatestAsync()).Blocked);
        h.Time.Advance(LotteryController.DrawDuration);
        Assert.True((await NextAsync()).CanSummon);
    }

    [Fact]
    public async Task Summoned_it_is_on_its_way_then_on_the_page_as_the_page_reports_it_set_off_and_away_once_it_left_or_the_pages_went()
    {
        var (updates, pet) = await ConnectedAsync();
        using var _ = pet;
        await using var __ = updates;
        var service = h.Client.WebService;

        await service.SummonAsync();
        await pet.NextAsync(DesktopPet.Summon);
        Assert.Equal(new PranksterSummon(null, PranksterVisit.OnTheWay), await NextAsync());
        Assert.Equal(HttpStatusCode.NoContent, await ReportAsync("start"));
        Assert.Equal(PranksterVisit.OnStage, (await NextAsync()).Visit);
        Assert.Equal(HttpStatusCode.BadRequest, await ReportAsync("nope"));
        Assert.Equal(HttpStatusCode.NoContent, await ReportAsync("end"));
        Assert.Equal(new PranksterSummon(null, PranksterVisit.Away), await NextAsync());

        await service.SummonAsync();
        Assert.Equal(PranksterVisit.OnTheWay, (await NextAsync()).Visit);
        await service.StopAsync();
        Assert.Equal(new PranksterSummon("先在直播姬里加好桌宠页", PranksterVisit.Away), await NextAsync());
    }

    [Fact]
    public async Task While_any_page_has_it_on_the_page_it_is_there_and_one_that_left_before_it_set_off_is_away()
    {
        var (updates, pet) = await ConnectedAsync();
        using var _ = pet;
        await using var __ = updates;
        using var another = await DesktopPetEvents.OpenAsync(h);
        await h.Client.WebService.SummonAsync();
        Assert.Equal(PranksterVisit.OnTheWay, (await NextAsync()).Visit);

        await ReportAsync("start");
        await ReportAsync("start");
        await ReportAsync("end");
        await h.Client.IdleAsync();
        Assert.Equal(PranksterVisit.OnStage, (await LatestAsync()).Visit);
        await ReportAsync("end");
        Assert.Equal(PranksterVisit.Away, (await NextAsync()).Visit);

        // A PK starting while it is on its way has the page say it left before it set off.
        await h.Client.WebService.SummonAsync();
        Assert.Equal(PranksterVisit.OnTheWay, (await NextAsync()).Visit);
        await ReportAsync("end");
        Assert.Equal(PranksterVisit.Away, (await NextAsync()).Visit);
    }

    [Fact]
    public async Task One_that_cannot_come_is_not_told_to_the_page()
    {
        await h.ApplySettingsAsync(("desktop_pet.enabled", "true"));
        using var pet = await DesktopPetEvents.OpenAsync(h);
        await pet.NextAsync("state");

        await h.Client.WebService.SummonAsync();
        await h.Client.WebService.TestShowAsync("entry", null, "星河");

        Assert.Equal(DesktopPet.Test, (await pet.NextAsync()).Name);
        Assert.Equal(new PranksterSummon("连上直播间才能叫它来：没人送礼，也没人能帮它", PranksterVisit.Away), await LatestAsync());
    }

    /// <summary>Turns the 桌宠 on, opens the 桌宠页 and watches a room: it can come.</summary>
    private async Task<(IAsyncEnumerator<WatchUpdate> Updates, DesktopPetEvents Pet)> ConnectedAsync()
    {
        var connected = await DesktopPetEvents.WatchAsync(h);
        while (!(await NextAsync()).CanSummon) { }
        return connected;
    }

    /// <summary>The page is told the lottery on air as it changes, in the step that tells the GUI what that changes.</summary>
    private static Task OnAirAsync(DesktopPetEvents pet, Func<JsonElement, bool> match) =>
        pet.NextStateAsync(state => state.GetProperty("lottery") is { ValueKind: JsonValueKind.Object } on && match(on));

    /// <summary>The next state the GUI is told that differs from the one before: a step that changes nothing may tell it again.</summary>
    private async Task<PranksterSummon> NextAsync()
    {
        do Assert.True(await Pending());
        while (Take() == last);
        return last = summons!.Current;
    }

    /// <summary>The state the GUI was told last, of all it has been told by now.</summary>
    private async Task<PranksterSummon> LatestAsync()
    {
        if (last is null) return await NextAsync();
        while (Pending().IsCompleted)
        {
            Assert.True(await Pending());
            last = Take();
        }
        return last;
    }

    private Task<bool> Pending()
    {
        summons ??= h.Client.WebService.GetSummonUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
        return next ??= summons.MoveNextAsync().AsTask();
    }

    private PranksterSummon Take()
    {
        next = null;
        return summons!.Current;
    }

    private async Task<HttpStatusCode> ReportAsync(string visit)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.PostAsync("/desktop-pet/report?visit=" + visit, null, h.Stop.Token);
        return response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        await h.DisposeAsync();
        // A read left waiting ends as the harness stops, before its reader can go.
        if (next is not null) await next.ContinueWith(_ => { }, TaskScheduler.Default);
        if (summons is not null) await summons.DisposeAsync();
    }
}
