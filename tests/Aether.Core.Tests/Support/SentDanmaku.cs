using System.Text.Json;
using System.Threading.Channels;

namespace Aether.Core.Tests.Support;

/// <summary>What the bot sends through msg/send, and the danmaku that make it send.</summary>
internal static class SentDanmaku
{
    public const string Accepted = "{\"code\":0,\"message\":\"\"}";
    /// <summary>A <see cref="CaptureSends"/> response that fails the request with a network error once its form is captured.</summary>
    public const string NetworkFailure = "network";
    /// <summary>The id <see cref="DanmakuAsync"/> gives a danmaku unless told otherwise; queries reply to it as replay_dmid.</summary>
    public const string DanmakuId = "12345678901234567890";

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
            var response = responses.Length == 0 ? Accepted : responses[Math.Min(attempt++, responses.Length - 1)];
            return response == NetworkFailure
                ? throw new HttpRequestException("test network failure") : FakeBilibiliHttp.Json(response);
        });
        return sent;
        static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }

    /// <summary>Holds the response to msg/send attempt <paramref name="attempt"/> (every attempt when null) after
    /// <see cref="CaptureSends"/> has seen it, until the returned source is set or the send is cancelled.</summary>
    public static TaskCompletionSource HoldSends(WatchHarness h, int? attempt = null)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        h.Http.Intercept("/msg/send", async (_, token, next) =>
        {
            var response = await next();
            if (++attempts == attempt || attempt is null) await release.Task.WaitAsync(token);
            return response;
        });
        return release;
    }

    /// <summary>Every msg/send request so far, including failed, held and interrupted ones.</summary>
    public static int SendAttempts(WatchHarness h) => h.Http.Requests.Count(r => r.Uri.AbsolutePath == "/msg/send");

    public static async Task<Dictionary<string, string>> NextSendAsync(Channel<Dictionary<string, string>> sent) =>
        await sent.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public static async Task AssertNoSendAsync(Channel<Dictionary<string, string>> sent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sent.Reader.ReadAsync(timeout.Token).AsTask());
    }

    /// <summary>Pushes a danmaku carrying a reply id, by default the blind box keyword, and waits until it is reported.</summary>
    public static async Task DanmakuAsync(WatchHarness h, IAsyncEnumerator<WatchUpdate> updates,
        string text = "今日盲盒", long uid = 10001, string id = DanmakuId)
    {
        object?[] meta = new object?[16];
        meta[15] = new { extra = JsonSerializer.Serialize(new { id_str = id }) };
        await h.Server.PushRoomMessageAsync(JsonSerializer.Serialize(new { cmd = "DANMU_MSG", info = new object[] { meta, text, new object[] { uid, "观众" } } }));
        Assert.True(await updates.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(text, Assert.IsType<Danmaku>(updates.Current).Content);
    }
}
