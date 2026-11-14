using System.Net;
using System.Text.Json;

namespace Aether.Core.Tests.Support;

/// <summary>The 桌宠页's events, read as a streaming software that opened the page reads them, by the page's own id.</summary>
internal sealed class DesktopPetEvents(HttpClient http, string id, HttpResponseMessage response, StreamReader reader, CancellationToken token) : IDisposable
{
    public static Task<DesktopPetEvents> OpenAsync(WatchHarness h) => OpenAsync(h.Client, h.Stop.Token);

    /// <summary>The 桌宠页 of <paramref name="client"/>'s web service, opened as a streaming software opens it.</summary>
    public static async Task<DesktopPetEvents> OpenAsync(AetherClient client, CancellationToken token)
    {
        var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(client.WebService.Status.Origin) };
        var id = Guid.NewGuid().ToString("N");
        var response = await http.GetAsync($"desktop-pet/events?page={id}", HttpCompletionOption.ResponseHeadersRead, token);
        return new(http, id, response, new StreamReader(await response.Content.ReadAsStreamAsync(token)), token);
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

    /// <summary>Heartbeats until <paramref name="done"/>, as they find a closed page gone: its client first drains the stream for
    /// up to 2 seconds, and TCP may take a few heartbeats to notice the close. The clock moves on only once a heartbeat's writes
    /// ended, which the next one waiting shows, so no write to a page that stays is ever in flight past its 5-second timeout; a
    /// write to the closed page that hangs instead is given 2 seconds, then that timeout.</summary>
    public static async Task BeatUntilAsync(WatchHarness h, Func<bool> done)
    {
        var beat = TimeSpan.FromSeconds(15);
        for (var i = 0; i < 100 && !done(); i++)
        {
            var beats = h.Time.TimerCount(beat);
            h.Time.Advance(beat);
            if (!await NextBeatAsync()) h.Time.Advance(TimeSpan.FromSeconds(5));
            await NextBeatAsync();

            async Task<bool> NextBeatAsync()
            {
                for (var wait = 0; wait < 20 && h.Time.TimerCount(beat) == beats; wait++) await Task.Delay(100, h.Stop.Token);
                return h.Time.TimerCount(beat) > beats;
            }
        }
    }

    public Task<(string Name, JsonElement Data)> NextAsync() => ServerSentEvents.Event(reader);

    /// <summary>The next event named <paramref name="name"/>, past any other: the danmaku the harness syncs with are told too.</summary>
    public async Task<JsonElement> NextAsync(string name)
    {
        while (true)
            if (await NextAsync() is var (next, data) && next == name) return data;
    }

    /// <summary>The next state that <paramref name="match"/>es, past any other event.</summary>
    public async Task<JsonElement> NextStateAsync(Func<JsonElement, bool> match)
    {
        while (true)
            if (await NextAsync("state") is var state && match(state)) return state;
    }

    /// <inheritdoc cref="NextAsync(string)"/>
    public async Task<T> NextAsync<T>(string name) => Assert.IsType<T>((await NextAsync(name)).Deserialize<T>(WebServer.Json));

    /// <summary>The names of the next <paramref name="count"/> events, in order.</summary>
    public async Task<string[]> NamesAsync(int count)
    {
        var names = new string[count];
        for (var i = 0; i < count; i++) names[i] = (await NextAsync()).Name;
        return names;
    }

    /// <summary>What the page reports to Core, <c>POST /desktop-pet/report</c> with <paramref name="query"/> and its id, sent
    /// from <paramref name="origin"/> when one is given.</summary>
    public async Task<HttpStatusCode> ReportAsync(string query, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"desktop-pet/report{query}{(query.Length > 0 ? '&' : '?')}page={id}");
        if (origin is not null) request.Headers.Add("Origin", origin);
        using var reply = await http.SendAsync(request, token);
        return reply.StatusCode;
    }

    public void Dispose()
    {
        reader.Dispose();
        response.Dispose();
        http.Dispose();
    }
}
