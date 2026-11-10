using System.Text.Json;

namespace Aether.Core.Tests.Support;

/// <summary>The 桌宠页's events, read as a streaming software that opened the page reads them.</summary>
internal sealed class DesktopPetEvents(HttpClient http, HttpResponseMessage response, StreamReader reader) : IDisposable
{
    public static async Task<DesktopPetEvents> OpenAsync(WatchHarness h)
    {
        var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        var response = await http.GetAsync("desktop-pet/events", HttpCompletionOption.ResponseHeadersRead, h.Stop.Token);
        return new(http, response, new StreamReader(await response.Content.ReadAsStreamAsync(h.Stop.Token)));
    }

    /// <summary>Turns the 桌宠 on, watches room 6 as the GUI does and opens the 桌宠页, read past its first state.</summary>
    public static async Task<(IAsyncEnumerator<WatchUpdate> Updates, DesktopPetEvents Pet)> WatchAsync(WatchHarness h,
        params (string Name, string Value)[] settings)
    {
        await h.LoginAsync();
        await h.ApplySettingsAsync([("desktop_pet.enabled", "true"), .. settings]);
        h.EnableStreamOverlay = true;
        var updates = await h.WatchConnectedAsync(includeGiftRounds: true);
        var pet = await OpenAsync(h);
        Assert.True((await pet.NextAsync("state")).GetProperty("roomConnected").GetBoolean());
        return (updates, pet);
    }

    public Task<(string Name, JsonElement Data)> NextAsync() => ServerSentEvents.Event(reader);

    /// <summary>The next event named <paramref name="name"/>, past any other: the danmaku the harness syncs with are told too.</summary>
    public async Task<JsonElement> NextAsync(string name)
    {
        while (true)
            if (await NextAsync() is var (next, data) && next == name) return data;
    }

    public void Dispose()
    {
        reader.Dispose();
        response.Dispose();
        http.Dispose();
    }
}
