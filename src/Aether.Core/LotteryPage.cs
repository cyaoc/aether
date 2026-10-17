using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aether.Core;

/// <summary>The 抽奖页: read-only, it only shows what <see cref="LotteryController"/> reports.</summary>
public sealed class LotteryPage(LotteryController lottery)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>SSE frames: a state snapshot first, then one per change, plus a draw event when a draw starts.</summary>
    internal async IAsyncEnumerable<string> EventsAsync([EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var update in lottery.GetUpdatesAsync(token))
        {
            yield return "event: state\ndata: " + JsonSerializer.Serialize(update.State, Json) + "\n\n";
            if (update.Draw is { } draw) yield return "event: draw\ndata: " + JsonSerializer.Serialize(draw, Json) + "\n\n";
            if (update.EntryDraw is { } entryDraw) yield return "event: draw\ndata: " + JsonSerializer.Serialize(entryDraw, Json) + "\n\n";
        }
    }

    internal async Task HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (request.HttpMethod != "GET" || request.Url!.AbsolutePath != "/lottery") { response.StatusCode = 404; return; }
        response.ContentType = "text/html; charset=utf-8";
        using var html = typeof(LotteryPage).Assembly.GetManifestResourceStream("Aether.Core.LotteryPage.html")!;
        response.ContentLength64 = html.Length;
        await html.CopyToAsync(response.OutputStream, token);
    }
}
