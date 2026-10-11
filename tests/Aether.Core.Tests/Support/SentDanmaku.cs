using System.Text.Json;
using System.Threading.Channels;

namespace Aether.Core.Tests.Support;

/// <summary>What the bot sends through msg/send, and the danmaku that make it send.</summary>
internal static class SentDanmaku
{
    public const string Accepted = "{\"code\":0,\"message\":\"\"}";

    /// <summary>Answers each msg/send with the next of <paramref name="responses"/> (the last one repeats; none means
    /// accepted) and yields each request's form.</summary>
    public static Channel<Dictionary<string, string>> CaptureSends(WatchHarness h, params string[] responses)
    {
        var sent = Channel.CreateUnbounded<Dictionary<string, string>>();
        var attempt = 0;
        h.Http.Intercept("/msg/send", async (request, token, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.live.bilibili.com/msg/send", request.RequestUri!.ToString());
            Assert.Equal("https://live.bilibili.com/", request.Headers.Referrer!.ToString());
            var cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.Contains("SESSDATA=saved-session", cookie);
            Assert.Contains("bili_jct=saved-csrf", cookie);
            var body = await request.Content!.ReadAsStringAsync(token);
            sent.Writer.TryWrite(body.Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Decode(pair[0]), pair => Decode(pair[1])));
            return FakeBilibiliHttp.Json(responses.Length == 0 ? Accepted
                : responses[Math.Min(attempt++, responses.Length - 1)]);
        });
        return sent;
        static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }

    public static async Task<Dictionary<string, string>> NextSendAsync(Channel<Dictionary<string, string>> sent) =>
        await sent.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public static async Task AssertNoSendAsync(Channel<Dictionary<string, string>> sent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sent.Reader.ReadAsync(timeout.Token).AsTask());
    }

    /// <summary>Pushes a danmaku carrying a reply id, by default the blind box keyword, and waits until it is reported.</summary>
    public static async Task DanmakuAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates,
        string text = "今日盲盒", long uid = 10001)
    {
        object?[] meta = new object?[16];
        meta[15] = new { extra = JsonSerializer.Serialize(new { id_str = "12345678901234567890" }) };
        await h.Server.PushRoomMessageAsync(JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { meta, text, new object[] { uid, "观众" } } }));
        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(text, Assert.IsType<Danmaku>(updates.Current).Content);
    }
}
