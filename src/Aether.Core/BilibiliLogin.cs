using System.Net;
using System.Text.Json;

namespace Aether.Core;

internal sealed class BilibiliLogin(HttpClient http)
{
    private readonly CookieContainer cookies = new();

    public async Task<(string Content, string Key)> GenerateAsync(CancellationToken cancellationToken)
    {
        using var document = await GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate", cancellationToken);
        var data = document.RootElement.GetProperty("data");
        return (data.GetProperty("url").GetString()!, data.GetProperty("qrcode_key").GetString()!);
    }

    public async Task<(int Code, string? RefreshToken)> PollAsync(string key, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(
            $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}", cancellationToken);
        var data = document.RootElement.GetProperty("data");
        var code = data.GetProperty("code").GetInt32();
        return (code, code == 0 ? data.GetProperty("refresh_token").GetString() : null);
    }

    public async Task<Dictionary<string, string>> GetCookiesAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        foreach (Cookie cookie in cookies.GetAllCookies()) result[cookie.Name] = cookie.Value;
        if (!result.ContainsKey("buvid3"))
        {
            using var document = await GetAsync("https://api.bilibili.com/x/frontend/finger/spi", cancellationToken);
            foreach (Cookie cookie in cookies.GetAllCookies()) result[cookie.Name] = cookie.Value;
            result["buvid3"] = document.RootElement.GetProperty("data").GetProperty("b_3").GetString()!;
        }
        foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "buvid3" })
            if (!result.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"登录响应缺少 {name} cookie。");
        return result;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Referrer = new Uri("https://www.bilibili.com/");
        var cookieHeader = cookies.GetCookieHeader(request.RequestUri!);
        if (cookieHeader.Length > 0) request.Headers.Add("Cookie", cookieHeader);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var code = document.RootElement.GetProperty("code").GetInt32();
        if (code == 0) return document;
        document.Dispose();
        throw new InvalidOperationException($"B站登录接口失败（{code}）。");
    }
}
