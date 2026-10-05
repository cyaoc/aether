using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.Core;

internal enum QrCodeState { Waiting, Expired, Confirmed }

/// <summary>One browser-like flow against B站's web API; its requests share a cookie jar.</summary>
internal sealed class BilibiliApi(HttpClient http, TimeProvider timeProvider)
{
    private const string LiveReferer = "https://live.bilibili.com/";
    private const string LoginReferer = "https://www.bilibili.com/";
    private readonly CookieContainer cookies = new();

    /// <summary>Fetches an anonymous buvid3 and adds it to the jar for later requests.</summary>
    public async Task<string> GetBuvidAsync(CancellationToken cancellationToken)
    {
        using var spi = await GetAsync("https://api.bilibili.com/x/frontend/finger/spi", LiveReferer, cancellationToken);
        CheckCode(spi, "获取匿名 buvid3");
        var buvid = spi.RootElement.GetProperty("data").GetProperty("b_3").GetString()!;
        cookies.Add(new Cookie("buvid3", buvid, "/", ".bilibili.com"));
        return buvid;
    }

    public async Task<(long Mid, string MixinKey)> GetNavigationAsync(CancellationToken cancellationToken)
    {
        using var nav = await GetAsync("https://api.bilibili.com/x/web-interface/nav", LiveReferer, cancellationToken);
        if (nav.RootElement.GetProperty("code").GetInt32() == -101) return (0, "");
        CheckCode(nav, "检查登录状态");
        var data = nav.RootElement.GetProperty("data");
        if (!data.GetProperty("isLogin").GetBoolean()) return (0, "");
        var images = data.GetProperty("wbi_img");
        var keys = Path.GetFileNameWithoutExtension(images.GetProperty("img_url").GetString())
            + Path.GetFileNameWithoutExtension(images.GetProperty("sub_url").GetString());
        int[] permutation = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13];
        var mixinKey = new string(permutation.Select(i => keys[i]).ToArray());
        return (data.GetProperty("mid").GetInt64(), mixinKey);
    }

    public async Task<(long RoomId, string Buvid, string Token, Uri Server)> GetConnectionAsync(
        long roomId, string mixinKey, CancellationToken cancellationToken)
    {
        using var room = await GetAsync($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId.ToString(CultureInfo.InvariantCulture)}", LiveReferer, cancellationToken);
        CheckCode(room, $"直播间 {roomId} 查询");
        var realRoomId = room.RootElement.GetProperty("data").GetProperty("room_id").GetInt64();
        var buvid = GetCookies()["buvid3"]; // Every saved credential has it; PollQrCodeAsync refuses logins without one.
        // These parameters are all numeric; their sorted query needs no escaping or character filtering.
        var query = FormattableString.Invariant($"id={realRoomId}&type=0&web_location=444.8&wts={timeProvider.GetUtcNow().ToUnixTimeSeconds()}");
        var signature = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey)));
        using var info = await GetAsync($"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?{query}&w_rid={signature}", LiveReferer, cancellationToken);
        CheckCode(info, "获取弹幕服务器");
        var data = info.RootElement.GetProperty("data");
        if (!data.TryGetProperty("host_list", out var hosts) || hosts.GetArrayLength() == 0)
            throw new InvalidOperationException("B站未返回弹幕服务器，可能触发了风控。");
        var host = hosts[0];
        var uri = new UriBuilder("wss", host.GetProperty("host").GetString()!, host.GetProperty("wss_port").GetInt32(), "/sub").Uri;
        return (realRoomId, buvid, data.GetProperty("token").GetString()!, uri);
    }

    public async Task<(string Url, string Key)> GenerateQrCodeAsync(CancellationToken cancellationToken)
    {
        using var document = await GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate", LoginReferer, cancellationToken);
        CheckCode(document, "生成登录二维码");
        var data = document.RootElement.GetProperty("data");
        return (data.GetProperty("url").GetString()!, data.GetProperty("qrcode_key").GetString()!);
    }

    /// <summary>On <see cref="QrCodeState.Confirmed"/> the jar holds every required login cookie.</summary>
    public async Task<(QrCodeState State, string? RefreshToken)> PollQrCodeAsync(string key, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(
            $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}", LoginReferer, cancellationToken);
        CheckCode(document, "查询扫码状态");
        var data = document.RootElement.GetProperty("data");
        var code = data.GetProperty("code").GetInt32();
        if (code is 86101 or 86090) return (QrCodeState.Waiting, null); // Not scanned; scanned but not confirmed.
        if (code == 86038) return (QrCodeState.Expired, null);
        if (code != 0) throw new InvalidOperationException($"扫码登录失败（{code}）：{data.GetProperty("message").GetString()}");
        var refreshToken = data.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidDataException("登录响应缺少 refresh_token。");
        var received = GetCookies();
        foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "buvid3" })
            if (!received.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"登录响应缺少 {name} cookie。");
        return (QrCodeState.Confirmed, refreshToken);
    }

    /// <summary>Invalidates the jar's SESSDATA on B站's side; the jar must hold a saved login.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://passport.bilibili.com/login/exit/v2")
        {
            Content = new FormUrlEncodedContent([new("biliCSRF", GetCookies()["bili_jct"])])
        };
        using var document = await SendAsync(request, LoginReferer, cancellationToken);
        CheckCode(document, "B站退出登录");
    }

    public void AddCookies(IReadOnlyDictionary<string, string> saved)
    {
        foreach (var (name, value) in saved) cookies.Add(new Cookie(name, value, "/", ".bilibili.com"));
    }

    public Dictionary<string, string> GetCookies()
    {
        var result = new Dictionary<string, string>();
        foreach (Cookie cookie in cookies.GetAllCookies()) result[cookie.Name] = cookie.Value;
        return result;
    }

    private Task<JsonDocument> GetAsync(string url, string referer, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), referer, cancellationToken);

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, string referer, CancellationToken cancellationToken)
    {
        using var owned = request;
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Referrer = new Uri(referer);
        var cookieHeader = cookies.GetCookieHeader(request.RequestUri!);
        if (cookieHeader.Length > 0) request.Headers.Add("Cookie", cookieHeader);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static void CheckCode(JsonDocument document, string operation)
    {
        var root = document.RootElement;
        var code = root.GetProperty("code").GetInt32();
        if (code != 0)
            throw new InvalidOperationException($"{operation}失败（{code}）：{root.GetProperty("message").GetString()}");
    }
}
