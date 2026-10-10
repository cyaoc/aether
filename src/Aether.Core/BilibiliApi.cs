using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    public async Task<long> ResolveRoomIdAsync(long roomId, CancellationToken cancellationToken)
    {
        var (_, room) = await GetAsync($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId.ToString(CultureInfo.InvariantCulture)}",
            LiveReferer, $"直播间 {roomId} 查询", cancellationToken);
        var realRoomId = room.GetProperty("room_id").GetInt64();
        if (realRoomId <= 0) throw new InvalidDataException("B站未返回有效的真实房间号。");
        return realRoomId;
    }

    public async Task<(long Mid, string Buvid, string Token, Uri Server)> GetConnectionAsync(
        long realRoomId, CancellationToken cancellationToken)
    {
        var buvid = await EnsureBuvidAsync(cancellationToken);
        var (mid, loginData) = await GetLoginStatusAndWbiKeysAsync(cancellationToken);
        var images = loginData.GetProperty("wbi_img");
        var keys = Path.GetFileNameWithoutExtension(images.GetProperty("img_url").GetString())
            + Path.GetFileNameWithoutExtension(images.GetProperty("sub_url").GetString());
        int[] permutation = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13];
        var mixinKey = new string(permutation.Select(i => keys[i]).ToArray());
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
        return (mid, buvid, data.GetProperty("token").GetString()!, uri);
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
        return (QrCodeState.Confirmed, IssuedCredential(data, "登录响应"));
    }

    /// <summary>Returns the timestamp to refresh with, or null when B站 does not ask for a refresh.</summary>
    public async Task<long?> GetRefreshTimestampAsync(CancellationToken cancellationToken)
    {
        var (_, data) = await GetAsync(
            $"https://passport.bilibili.com/x/passport-login/web/cookie/info?csrf={Uri.EscapeDataString(credential!.Cookies["bili_jct"])}",
            LoginReferer, "检查登录凭据刷新", cancellationToken);
        return data.GetProperty("refresh").GetBoolean() ? data.GetProperty("timestamp").GetInt64() : null;
    }

    public async Task<Credential> RefreshCredentialAsync(long timestamp, CancellationToken cancellationToken)
    {
        // Protocol key: bilibili-API-collect/docs/login/cookie_refresh.md (archived master).
        using var rsa = RSA.Create();
        rsa.ImportFromPem("""
            -----BEGIN PUBLIC KEY-----
            MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0Eg
            Uc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71
            nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40
            JNrRuoEUXpabUzGB8QIDAQAB
            -----END PUBLIC KEY-----
            """);
        var path = Convert.ToHexStringLower(rsa.Encrypt(
            Encoding.UTF8.GetBytes(FormattableString.Invariant($"refresh_{timestamp}")), RSAEncryptionPadding.OaepSHA256));
        var html = await SendTextAsync(new HttpRequestMessage(HttpMethod.Get,
            $"https://www.bilibili.com/correspond/1/{path}"), LoginReferer, cancellationToken);
        var match = Regex.Match(html, """<div\b[^>]*\sid\s*=\s*["']1-name["'][^>]*>([^<]+)</div\s*>""",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var refreshCsrf = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        if (refreshCsrf.Length == 0) throw new InvalidDataException("刷新页面缺少 refresh_csrf。");
        var (_, data) = await PostAsync("https://passport.bilibili.com/x/passport-login/web/cookie/refresh", [
            new("csrf", credential!.Cookies["bili_jct"]), new("refresh_csrf", refreshCsrf),
            new("source", "main_web"), new("refresh_token", credential.RefreshToken)],
            "刷新登录凭据", cancellationToken, credentialRefresh: true);
        return IssuedCredential(data, "刷新响应");
    }

    /// <summary>The credential B站 just issued: its refresh_token plus this flow's cookies, which keep any it did not resend.</summary>
    private Credential IssuedCredential(JsonElement data, string response)
    {
        var refreshToken = data.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidDataException($"{response}缺少 refresh_token。");
        var received = GetCookies();
        foreach (var name in new[] { "SESSDATA", "bili_jct", "DedeUserID", "buvid3" })
            if (!received.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"{response}缺少 {name} cookie。");
        var now = timeProvider.GetUtcNow();
        return new Credential(received, refreshToken, now, now);
    }

    public async Task ConfirmCredentialRefreshAsync(string previousToken, CancellationToken cancellationToken)
    {
        await PostAsync("https://passport.bilibili.com/x/passport-login/web/confirm/refresh", [
            new("csrf", credential!.Cookies["bili_jct"]), new("refresh_token", previousToken)],
            "确认登录凭据刷新", cancellationToken);
    }

    /// <summary>Invalidates the supplied credential on B站; an anonymous identity has nothing to sign out.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (credential is null) return;
        await PostAsync("https://passport.bilibili.com/login/exit/v2", [new("biliCSRF", GetCookies()["bili_jct"])],
            "B站退出登录", cancellationToken);
    }

    private Dictionary<string, string> GetCookies()
    {
        var result = new Dictionary<string, string>();
        foreach (Cookie cookie in cookies.GetAllCookies()) result[cookie.Name] = cookie.Value;
        return result;
    }

    /// <summary>Sends <paramref name="message"/> as a reply to <paramref name="trigger"/>'s sender.</summary>
    public async Task ReplyAsync(long roomId, Danmaku trigger, string message, CancellationToken cancellationToken)
    {
        var csrf = credential?.Cookies["bili_jct"] ?? throw new InvalidOperationException("缺少登录凭据。");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.live.bilibili.com/msg/send")
        {
            Content = new FormUrlEncodedContent([
                new("roomid", roomId.ToString(CultureInfo.InvariantCulture)), new("msg", message),
                new("rnd", timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                new("fontsize", "25"), new("color", "16777215"), new("mode", "1"), new("bubble", "0"),
                new("csrf", csrf), new("csrf_token", csrf), new("reply_mid", trigger.Uid.ToString(CultureInfo.InvariantCulture)),
                new("replay_dmid", trigger.Id), new("reply_uname", ""), new("reply_attr", "0"),
            ]),
        };
        await SendAsync(request, LiveReferer, "发送弹幕", cancellationToken, sendingDanmaku: true);
    }

    private Task<(int Code, JsonElement Data)> GetAsync(string url, string referer, string operation,
        CancellationToken cancellationToken, int? acceptedCode = null) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), referer, operation, cancellationToken, acceptedCode);

    /// <summary>Passport form posts always carry the login referer.</summary>
    private Task<(int Code, JsonElement Data)> PostAsync(string url, IEnumerable<KeyValuePair<string, string>> form,
        string operation, CancellationToken cancellationToken, bool credentialRefresh = false) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            LoginReferer, operation, cancellationToken, credentialRefresh: credentialRefresh);

    /// <param name="sendingDanmaku">msg/send's own failure rules apply: see <see cref="CheckDanmakuSent"/>.</param>
    private async Task<(int Code, JsonElement Data)> SendAsync(HttpRequestMessage request, string referer, string operation,
        CancellationToken cancellationToken, int? acceptedCode = null, bool credentialRefresh = false, bool sendingDanmaku = false)
    {
        using var document = JsonDocument.Parse(await SendTextAsync(request, referer, cancellationToken));
        var root = document.RootElement;
        var code = root.GetProperty("code").GetInt32();
        if (credentialRefresh && code != 0)
            throw new CredentialRejectedException($"{operation}被拒绝（{code}）。");
        if (sendingDanmaku) CheckDanmakuSent(root, code, operation);
        CheckCode(root, operation, acceptedCode);
        return (code, root.TryGetProperty("data", out var data) ? data.Clone() : default);
    }

    /// <summary>A rate limit throws <see cref="DanmakuRateLimitedException"/>; code 0 with a non-empty message is a
    /// filtered or refused danmaku. Sources: docs/research/blind-box-gift-events.md sections 6 and 7.</summary>
    private static void CheckDanmakuSent(JsonElement root, int code, string operation)
    {
        var message = root.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        // 10031 is BAC's "too frequent"; sources disagree whether 10030 or 10031 means a repeat, so both count.
        // B站's 2019 backend answers code 0 with "msg in 1s" or "msg repeat"; Danmuji and MagicalDanmaku handle the same.
        if (code is 10031 or 10030 || code == 0 && message is "msg in 1s" or "msg repeat")
            throw new DanmakuRateLimitedException($"{operation}受频率限制（{code}）：{message}");
        if (code == 0 && message is not "")
            throw new InvalidOperationException($"{operation}失败：{message}");
    }

    private async Task<string> SendTextAsync(HttpRequestMessage request, string referer, CancellationToken cancellationToken)
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
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static int CheckCode(JsonElement root, string operation, int? acceptedCode = null)
    {
        var code = root.GetProperty("code").GetInt32();
        if (code != 0 && code != acceptedCode)
            throw new InvalidOperationException($"{operation}失败（{code}）：{root.GetProperty("message").GetString()}");
        return code;
    }
}

internal sealed class CredentialRejectedException(string message) : Exception(message);
internal sealed class DanmakuRateLimitedException(string message) : Exception(message);
