using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.Core;

internal enum QrCodeState { Waiting, Expired, Confirmed }

/// <summary>One browser-like flow using a fixed credential, or an anonymous identity.</summary>
internal sealed class BilibiliApi
{
    private const string LiveReferer = "https://live.bilibili.com/";
    private const string LoginReferer = "https://www.bilibili.com/";
    private readonly HttpClient http;
    private readonly TimeProvider timeProvider;
    private readonly Credential? credential;
    private readonly CookieContainer cookies = new();
    private (long Mid, JsonElement Data)? loginStatus;

    public BilibiliApi(HttpClient http, TimeProvider timeProvider, Credential? credential)
    {
        this.http = http;
        this.timeProvider = timeProvider;
        this.credential = credential;
        if (credential is not null)
            foreach (var (name, value) in credential.Cookies)
                cookies.Add(new Cookie(name, value, "/", ".bilibili.com"));
    }

    private async Task<string> EnsureBuvidAsync(CancellationToken cancellationToken)
    {
        if (GetCookies().TryGetValue("buvid3", out var buvid)) return buvid;
        var (_, data) = await GetAsync("https://api.bilibili.com/x/frontend/finger/spi", LiveReferer, "获取匿名 buvid3", cancellationToken);
        buvid = data.GetProperty("b_3").GetString()!;
        cookies.Add(new Cookie("buvid3", buvid, "/", ".bilibili.com"));
        return buvid;
    }

    public async Task<bool> IsLoggedInAsync(CancellationToken cancellationToken) =>
        credential is not null && (await GetLoginStatusAndWbiKeysAsync(cancellationToken)).Mid != 0;

    private async Task<(long Mid, JsonElement Data)> GetLoginStatusAndWbiKeysAsync(CancellationToken cancellationToken)
    {
        if (loginStatus is { } cached) return cached;
        var (code, data) = await GetAsync("https://api.bilibili.com/x/web-interface/nav", LiveReferer,
            "检查登录状态", cancellationToken, acceptedCode: -101);
        var loggedIn = code == 0 && data.GetProperty("isLogin").GetBoolean();
        var status = (loggedIn ? data.GetProperty("mid").GetInt64() : 0, data);
        loginStatus = status;
        return status;
    }

    public async Task<(long Mid, long RoomId, string Buvid, string Token, Uri Server)> GetConnectionAsync(
        long roomId, CancellationToken cancellationToken)
    {
        var buvid = await EnsureBuvidAsync(cancellationToken);
        var (mid, loginData) = await GetLoginStatusAndWbiKeysAsync(cancellationToken);
        var images = loginData.GetProperty("wbi_img");
        var keys = Path.GetFileNameWithoutExtension(images.GetProperty("img_url").GetString())
            + Path.GetFileNameWithoutExtension(images.GetProperty("sub_url").GetString());
        int[] permutation = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13];
        var mixinKey = new string(permutation.Select(i => keys[i]).ToArray());
        var (_, room) = await GetAsync($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId.ToString(CultureInfo.InvariantCulture)}",
            LiveReferer, $"直播间 {roomId} 查询", cancellationToken);
        var realRoomId = room.GetProperty("room_id").GetInt64();
        // These parameters are all numeric; their sorted query needs no escaping or character filtering.
        var query = FormattableString.Invariant($"id={realRoomId}&type=0&web_location=444.8&wts={timeProvider.GetUtcNow().ToUnixTimeSeconds()}");
        var signature = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey)));
        var (_, data) = await GetAsync($"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?{query}&w_rid={signature}",
            LiveReferer, "获取弹幕服务器", cancellationToken);
        if (!data.TryGetProperty("host_list", out var hosts) || hosts.GetArrayLength() == 0)
            throw new InvalidOperationException("B站未返回弹幕服务器，可能触发了风控。");
        // ponytail: Only the first server is tried; if it stays unavailable, add failover across host_list.
        var host = hosts[0];
        var uri = new UriBuilder("wss", host.GetProperty("host").GetString()!, host.GetProperty("wss_port").GetInt32(), "/sub").Uri;
        return (mid, realRoomId, buvid, data.GetProperty("token").GetString()!, uri);
    }

    public async Task<(string Url, string Key)> GenerateQrCodeAsync(CancellationToken cancellationToken)
    {
        await EnsureBuvidAsync(cancellationToken);
        var (_, data) = await GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/generate",
            LoginReferer, "生成登录二维码", cancellationToken);
        return (data.GetProperty("url").GetString()!, data.GetProperty("qrcode_key").GetString()!);
    }

    /// <summary>On confirmation, returns the complete new credential ready to save.</summary>
    public async Task<(QrCodeState State, Credential? Credential)> PollQrCodeAsync(string key, CancellationToken cancellationToken)
    {
        var (_, data) = await GetAsync(
            $"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key={Uri.EscapeDataString(key)}",
            LoginReferer, "查询扫码状态", cancellationToken);
        var code = data.GetProperty("code").GetInt32();
        if (code is 86101 or 86090) return (QrCodeState.Waiting, null); // Not scanned; scanned but not confirmed.
        if (code == 86038) return (QrCodeState.Expired, null);
        CheckCode(data, "扫码登录");
        var refreshToken = data.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidDataException("登录响应缺少 refresh_token。");
        var received = GetCookies();
        foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "buvid3" })
            if (!received.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"登录响应缺少 {name} cookie。");
        return (QrCodeState.Confirmed, new Credential(received, refreshToken, timeProvider.GetUtcNow()));
    }

    /// <summary>Invalidates the supplied credential on B站; an anonymous identity has nothing to sign out.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (credential is null) return;
        var request = new HttpRequestMessage(HttpMethod.Post, "https://passport.bilibili.com/login/exit/v2")
        {
            Content = new FormUrlEncodedContent([new("biliCSRF", GetCookies()["bili_jct"])])
        };
        await SendAsync(request, LoginReferer, "B站退出登录", cancellationToken);
    }

    private Dictionary<string, string> GetCookies()
    {
        var result = new Dictionary<string, string>();
        foreach (Cookie cookie in cookies.GetAllCookies()) result[cookie.Name] = cookie.Value;
        return result;
    }

    private Task<(int Code, JsonElement Data)> GetAsync(string url, string referer, string operation,
        CancellationToken cancellationToken, int? acceptedCode = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), referer, operation, cancellationToken, acceptedCode);

    private async Task<(int Code, JsonElement Data)> SendAsync(HttpRequestMessage request, string referer, string operation,
        CancellationToken cancellationToken, int? acceptedCode = null)
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var code = CheckCode(root, operation, acceptedCode);
        return (code, root.TryGetProperty("data", out var data) ? data.Clone() : default);
    }

    private static int CheckCode(JsonElement root, string operation, int? acceptedCode = null)
    {
        var code = root.GetProperty("code").GetInt32();
        if (code != 0 && code != acceptedCode)
            throw new InvalidOperationException($"{operation}失败（{code}）：{root.GetProperty("message").GetString()}");
        return code;
    }
}
