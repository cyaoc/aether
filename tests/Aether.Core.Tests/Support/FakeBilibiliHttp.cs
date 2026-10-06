using System.Net;

namespace Aether.Core.Tests.Support;

internal sealed class FakeBilibiliHttp : HttpMessageHandler
{
    public Dictionary<string, string> Responses { get; } = new();
    public List<(Uri Uri, string? Cookie)> Requests { get; } = [];
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

    public void ConfigureLogin(string sessdata = "saved-session", string csrf = "saved-csrf",
        string buvid = "saved-buvid", params string[] extraCookies)
    {
        Responses["https://api.bilibili.com/x/frontend/finger/spi"] =
            System.Text.Json.JsonSerializer.Serialize(new { code = 0, data = new { b_3 = buvid } });
        Responses["https://passport.bilibili.com/x/passport-login/web/qrcode/generate"] =
            """{"code":0,"data":{"url":"https://example.test/scan","qrcode_key":"test-key"}}""";
        Responses["https://passport.bilibili.com/x/passport-login/web/qrcode/poll"] =
            """{"code":0,"data":{"code":0,"refresh_token":"refresh-token"}}""";
        Responses["https://passport.bilibili.com/login/exit/v2"] =
            """{"code":0,"status":true,"ts":1702204169,"data":{"redirectUrl":"https://www.bilibili.com/"}}""";
        Respond = (request, _) =>
        {
            var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
            var body = Responses.TryGetValue(path, out var response)
                ? response : throw new InvalidOperationException($"Unexpected HTTP request: {request.RequestUri}");
            return Task.FromResult(path.EndsWith("/qrcode/poll", StringComparison.Ordinal)
                ? Json(body, [
                    $"SESSDATA={sessdata}; Path=/; Domain=.bilibili.com; HttpOnly; Secure",
                    $"bili_jct={csrf}; Path=/; Domain=.bilibili.com",
                    "DedeUserID=123; Path=/; Domain=.bilibili.com",
                    "sid=extra-cookie; Path=/; Domain=.bilibili.com",
                    ..extraCookies])
                : Json(body));
        };
    }

    public static HttpResponseMessage Json(string body, params string[] cookies)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (cookies.Length > 0) response.Headers.Add("Set-Cookie", cookies);
        return response;
    }

    /// <summary>Throws <paramref name="error"/> for the first request to <paramref name="path"/>; later requests go to <see cref="Respond"/> as before.</summary>
    public void FailOnce(string path, Exception error)
    {
        var respond = Respond!;
        var failed = false;
        Respond = (request, token) =>
        {
            if (failed || request.RequestUri!.AbsolutePath != path) return respond(request, token);
            failed = true;
            throw error;
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add((uri, request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null));
        if (Respond is not null) return Respond(request, cancellationToken);
        var body = Responses.TryGetValue(uri.GetLeftPart(UriPartial.Path), out var response)
            ? response
            : throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
