using System.Text.Json;

namespace Aether.Core.Tests.Support;

internal static class ServerSentEvents
{
    /// <summary>Reads up to the next <c>data:</c> line, with the event name sent before it.</summary>
    public static async Task<(string Name, JsonElement Data)> Event(StreamReader reader)
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
}
