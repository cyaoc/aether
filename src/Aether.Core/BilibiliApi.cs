using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.Core;

internal sealed class BilibiliApi(HttpClient http, TimeProvider timeProvider)
{
    public async Task<(long RoomId, string Buvid, string Token, Uri Server)> GetConnectionAsync(long roomId, CancellationToken cancellationToken)
    {
        using var room = await GetAsync($"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId.ToString(CultureInfo.InvariantCulture)}", null, cancellationToken);
        CheckCode(room, $"直播间 {roomId} 查询");
        var realRoomId = room.RootElement.GetProperty("data").GetProperty("room_id").GetInt64();

        using var spi = await GetAsync("https://api.bilibili.com/x/frontend/finger/spi", null, cancellationToken);
        CheckCode(spi, "获取匿名 buvid3");
        var buvid = spi.RootElement.GetProperty("data").GetProperty("b_3").GetString()!;
        using var nav = await GetAsync("https://api.bilibili.com/x/web-interface/nav", buvid, cancellationToken);
        // Anonymous nav returns -101 but still supplies the WBI keys.
        if (nav.RootElement.GetProperty("code").GetInt32() != -101) CheckCode(nav, "获取 wbi key");
        var images = nav.RootElement.GetProperty("data").GetProperty("wbi_img");
        var keys = Path.GetFileNameWithoutExtension(images.GetProperty("img_url").GetString())
            + Path.GetFileNameWithoutExtension(images.GetProperty("sub_url").GetString());
        int[] permutation = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
            27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13];
        var mixinKey = new string(permutation.Select(i => keys[i]).ToArray());
        // These parameters are all numeric; their sorted query needs no escaping or character filtering.
        var query = FormattableString.Invariant($"id={realRoomId}&type=0&web_location=444.8&wts={timeProvider.GetUtcNow().ToUnixTimeSeconds()}");
        var signature = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(query + mixinKey)));
        using var info = await GetAsync($"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?{query}&w_rid={signature}", buvid, cancellationToken);
        CheckCode(info, "获取弹幕服务器");
        var data = info.RootElement.GetProperty("data");
        if (!data.TryGetProperty("host_list", out var hosts) || hosts.GetArrayLength() == 0)
            throw new InvalidOperationException("B站未返回弹幕服务器，可能触发了风控。");
        var host = hosts[0];
        var uri = new UriBuilder("wss", host.GetProperty("host").GetString()!, host.GetProperty("wss_port").GetInt32(), "/sub").Uri;
        return (realRoomId, buvid, data.GetProperty("token").GetString()!, uri);
    }

    private async Task<JsonDocument> GetAsync(string url, string? buvid, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        request.Headers.Referrer = new Uri("https://live.bilibili.com/");
        if (buvid is not null) request.Headers.Add("Cookie", $"buvid3={buvid}");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
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
