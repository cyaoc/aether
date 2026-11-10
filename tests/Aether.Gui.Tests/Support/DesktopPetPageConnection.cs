using System.Text.Json;

namespace Aether.Gui.Tests.Support;

/// <summary>The 桌宠页 opened as a streaming software opens it: the web service counts it, and a 试演 reaches it.</summary>
internal sealed class DesktopPetPageConnection(HttpClient http, HttpResponseMessage response, StreamReader reader) : IDisposable
{
    public static async Task<DesktopPetPageConnection> OpenAsync(GuiHarness harness)
    {
        var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(harness.Client.WebService.Status.Origin) };
        var response = await http.GetAsync("desktop-pet/events", HttpCompletionOption.ResponseHeadersRead);
        return new(http, response, new StreamReader(await response.Content.ReadAsStreamAsync()));
    }

    /// <summary>The data of the next event named <paramref name="name"/>, past any other.</summary>
    public async Task<JsonElement> NextAsync(string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var current = "";
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ")) current = line[7..];
            if (line.StartsWith("data: ") && current == name) return JsonSerializer.Deserialize<JsonElement>(line[6..]);
        }
        throw new EndOfStreamException();
    }

    public void Dispose()
    {
        reader.Dispose();
        response.Dispose();
        http.Dispose();
    }
}
