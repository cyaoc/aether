using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aether.Core;

/// <summary>The 抽奖页: read-only, it only shows what <see cref="LotteryController"/> reports.</summary>
public sealed class LotteryPage(WebService service, LotteryController lottery)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>SSE frames: a state snapshot first, then one per change, plus a draw event when a draw starts.</summary>
    internal async IAsyncEnumerable<string> EventsAsync([EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var update in lottery.GetUpdatesAsync(token))
        {
            // Once the room connection ends nobody can stop or hide what is on air, so the page clears until it reconnects.
            var live = update.State.CanControl ? update.State : update.State with { PoolLottery = null, EntryLottery = null };
            var state = (update.Example ?? live) with { Theme = update.State.Theme, IsExamplePlaying = update.State.IsExamplePlaying };
            yield return "event: state\ndata: " + JsonSerializer.Serialize(state, Json) + "\n\n";
            if (update.Draw is { } draw) yield return "event: draw\ndata: " + JsonSerializer.Serialize(draw, Json) + "\n\n";
            if (update.EntryDraw is { } entryDraw) yield return "event: draw\ndata: " + JsonSerializer.Serialize(entryDraw, Json) + "\n\n";
        }
    }

    internal async Task HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        var (resource, contentType) = request.Url!.AbsolutePath switch
        {
            "/lottery" => ("LotteryPage.html", "text/html; charset=utf-8"),
            "/lottery/style.css" => ("LotteryPage.css", "text/css; charset=utf-8"),
            "/lottery/mecha.css" => ("LotteryPage.Mecha.css", "text/css; charset=utf-8"),
            "/lottery/gacha.css" => ("LotteryPage.Gacha.css", "text/css; charset=utf-8"),
            "/lottery/page.js" => ("LotteryPage.js", "text/javascript; charset=utf-8"),
            "/lottery/SmileySans-Oblique.woff2" => ("Fonts.SmileySans-Oblique.woff2", "font/woff2"),
            "/lottery/OFL.txt" => ("Fonts.OFL.txt", "text/plain; charset=utf-8"),
            "/lottery/ZCOOLKuaiLe-Regular.ttf" => ("Fonts.ZCOOLKuaiLe-Regular.ttf", "font/ttf"),
            "/lottery/ZCOOLKuaiLe-OFL.txt" => ("Fonts.ZCOOLKuaiLe-OFL.txt", "text/plain; charset=utf-8"),
            _ => (null, null)
        };
        if (request.HttpMethod != "GET" || resource is null) { response.StatusCode = 404; return; }
        response.ContentType = contentType;
        using var content = typeof(LotteryPage).Assembly.GetManifestResourceStream("Aether.Core." + resource)!;
        response.ContentLength64 = content.Length;
        await content.CopyToAsync(response.OutputStream, token);
    }

    public void PlayExample()
    {
        lock (service.Gate)
        {
            if (!service.Status.Running)
                throw new InvalidOperationException(service.Status.Error ?? "网页服务没有运行，请先在通用设置中打开网页服务。");
            lottery.PlayExample();
        }
    }

    internal void StopExample() => lottery.StopExample();
}
