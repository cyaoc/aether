using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

internal static class DanmakuProtocol
{
    public const int MaxPacketSize = 8 * 1024 * 1024;
    private const int MaxLoggedJsonBytes = 2048;

    private enum Operation
    {
        Heartbeat = 2,
        RoomMessage = 5,
        Authentication = 7,
        AuthenticationReply = 8
    }

    internal abstract record DecodedEvent;
    internal sealed record AuthenticationReply(bool Success) : DecodedEvent;
    internal sealed record DanmakuReceived(Danmaku Danmaku) : DecodedEvent;
    internal sealed record BlindBoxReceived(BlindBox BlindBox) : DecodedEvent;

    /// <summary>What one room connection has already logged about B站's message formats; reconnects keep it.</summary>
    internal sealed class FormatNotices
    {
        private string? giftCommand;
        private readonly HashSet<string> unknownCommands = [];

        public void Observe(string command, ILogger logger)
        {
            if (command is "SEND_GIFT" or "SEND_GIFT_V2")
            {
                if (command == giftCommand) return;
                giftCommand = command;
                logger.LogInformation("本直播间的礼物消息为 {Version}（{Command}）", command == "SEND_GIFT" ? "V1" : "V2", command);
            }
            // B站 moves message kinds to new formats room by room without notice; say so instead of silently missing them.
            else if ((command.StartsWith("DANMU_MSG", StringComparison.Ordinal) || command.StartsWith("SEND_GIFT", StringComparison.Ordinal))
                && !IsDanmaku(command) && command != "DANMU_MSG_MIRROR" && unknownCommands.Add(command))
                logger.LogWarning("收到不认识的直播间消息 {Command}，弹幕或礼物的消息格式可能变了", command);
        }
    }

    public static byte[] CreateAuthentication(long mid, long roomId, string token, string buvid) =>
        Pack(Operation.Authentication, JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = mid, roomid = roomId, protover = 3, platform = "web", type = 2,
            key = token, buvid
        }));

    public static byte[] CreateHeartbeat() => Pack(Operation.Heartbeat, []);

    public static IEnumerable<DecodedEvent> Decode(
        byte[] bytes, DateTimeOffset receivedAt, bool authenticated, FormatNotices notices, ILogger logger)
    {
        foreach (var packet in Unpack(bytes))
        {
            if (packet.Operation == Operation.AuthenticationReply)
            {
                using var auth = JsonDocument.Parse(packet.Body);
                var success = auth.RootElement.GetProperty("code").GetInt32() == 0;
                // Authentication and room messages can share one (possibly compressed) frame.
                if (success) authenticated = true;
                yield return new AuthenticationReply(success);
            }
            else if (packet.Operation == Operation.RoomMessage && authenticated
                && ParseRoomMessage(packet.Body, receivedAt, notices, logger) is { } decoded)
                yield return decoded;
        }
    }

    private static bool IsDanmaku(string command) =>
        command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal);

    private static DecodedEvent? ParseRoomMessage(
        ReadOnlyMemory<byte> body, DateTimeOffset receivedAt, FormatNotices notices, ILogger logger)
    {
        string? command = null;
        DecodedEvent? decoded = null;
        try
        {
            using var message = JsonDocument.Parse(body);
            var root = message.RootElement;
            command = root.GetProperty("cmd").GetString() ?? throw new JsonException("缺少 cmd。");
            notices.Observe(command, logger);
            if (IsDanmaku(command))
            {
                var info = root.GetProperty("info");
                var nickname = info[2][1].GetString() ?? throw new JsonException("缺少弹幕昵称。");
                var content = info[1].GetString() ?? throw new JsonException("缺少弹幕内容。");
                var uid = info[2][0].GetInt64();
                if (uid < 0) throw new JsonException("弹幕 uid 无效。");
                var id = "";
                // Older messages have no extra; keep displaying them and send an empty replay_dmid.
                if (info[0].GetArrayLength() > 15 && info[0][15].TryGetProperty("extra", out var extra))
                {
                    using var details = JsonDocument.Parse(extra.GetString() ?? throw new JsonException("弹幕 extra 无效。"));
                    if (details.RootElement.TryGetProperty("id_str", out var value)) id = value.GetString() ?? "";
                }
                decoded = new DanmakuReceived(new Danmaku(receivedAt, nickname, content) { Uid = uid, Id = id });
            }
            else if (command == "SEND_GIFT" && ParseBlindBox(root.GetProperty("data"), body, logger) is { } blindBox)
                decoded = new BlindBoxReceived(blindBox);
        }
        // Only JSON parsing and field access are inside this boundary; frame errors still end the stream.
        catch (Exception error) when (error is JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            var length = Math.Min(body.Length, MaxLoggedJsonBytes);
            // Keep a complete UTF-8 prefix when the byte limit lands inside a character.
            while (length < body.Length && length > 0 && (body.Span[length] & 0xC0) == 0x80) length--;
            var json = Encoding.UTF8.GetString(body.Span[..length]);
            logger.LogWarning("跳过无法解析的直播间消息（cmd: {Command}）：{Error}；原始 JSON：{Json}{Truncated}",
                command, error.Message, json, length < body.Length ? "（已截断，最多 2KB）" : "");
            return null;
        }
        // Valid JSON only has literal CR/LF outside strings; keep each capture on one line.
        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("直播间消息原始 JSON：{Json}", Encoding.UTF8.GetString(body.Span).Replace("\r", "").Replace("\n", ""));
        if (decoded is null) logger.LogDebug("忽略直播间事件 {Command}", command);
        return decoded;
    }

    private static BlindBox? ParseBlindBox(JsonElement data, ReadOnlyMemory<byte> body, ILogger logger)
    {
        if (!data.TryGetProperty("blind_gift", out var blind) || blind.ValueKind == JsonValueKind.Null) return null;
        if (data.GetProperty("coin_type").GetString() is var coinType && coinType != "gold")
        {
            // No source has ever shown a non-gold blind box; skip it, but loudly.
            logger.LogWarning("收到 coin_type 为 {CoinType} 的盲盒，从未见过这种情况，未记录", coinType);
            return null;
        }
        var uid = data.GetProperty("uid").GetInt64();
        var blindId = blind.GetProperty("original_gift_id").GetInt64();
        var blindPrice = blind.GetProperty("original_gift_price").GetInt64();
        var giftId = data.GetProperty("giftId").GetInt64();
        var price = data.GetProperty("price").GetInt64();
        var num = data.GetProperty("num").GetInt64();
        var spend = data.GetProperty("total_coin").GetInt64();
        var timestamp = data.GetProperty("timestamp").GetInt64();
        if (uid < 0 || blindId <= 0 || giftId <= 0 || blindPrice < 0 || price < 0 || num <= 0 || spend < 0 || timestamp < 0)
            throw new JsonException("盲盒的标识、金额、个数或送出时间无效。");
        var tid = !data.TryGetProperty("tid", out var id) ? null
            : id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString(); // BAC types it as num; samples are strings.
        return new BlindBox(
            Uid: uid,
            Nickname: data.GetProperty("uname").GetString() ?? throw new JsonException("缺少观众昵称。"),
            BlindGiftId: blindId,
            BlindGiftName: blind.GetProperty("original_gift_name").GetString() ?? throw new JsonException("缺少盲盒名称。"),
            BlindGiftPrice: blindPrice,
            OpenedGiftId: giftId,
            OpenedGiftName: data.GetProperty("giftName").GetString() ?? throw new JsonException("缺少开出礼物名称。"),
            OpenedGiftPrice: price,
            Num: num,
            Spend: spend,
            OpenedValue: checked(price * num),
            Timestamp: timestamp,
            Tid: string.IsNullOrEmpty(tid) ? null : tid,
            Shown: !data.TryGetProperty("switch", out var shown) || shown.ValueKind != JsonValueKind.False,
            RawMessage: Encoding.UTF8.GetString(body.Span));
    }

    private static IEnumerable<(Operation Operation, ReadOnlyMemory<byte> Body)> Unpack(byte[] bytes, int depth = 0)
    {
        if (depth > 4) throw new InvalidDataException("弹幕压缩包嵌套过深。");
        for (var offset = 0; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 16) throw new InvalidDataException("弹幕包头不完整。");
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            var headerLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
            if (headerLength < 16 || length < headerLength || length > bytes.Length - offset)
                throw new InvalidDataException("弹幕包长度无效。");
            var operation = (Operation)BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8));
            var version = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 6));
            var body = bytes.AsMemory(offset + headerLength, length - headerLength);
            if (version is 2 or 3)
            {
                using var input = new MemoryStream(body.ToArray());
                using Stream decompressor = version == 2
                    ? new ZLibStream(input, CompressionMode.Decompress)
                    : new BrotliStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[16384];
                int count;
                while ((count = decompressor.Read(buffer)) != 0)
                {
                    if (output.Length + count > MaxPacketSize)
                        throw new InvalidDataException("弹幕解压数据过大。");
                    output.Write(buffer, 0, count);
                }
                foreach (var packet in Unpack(output.ToArray(), depth + 1)) yield return packet;
            }
            else if (version is 0 or 1) yield return (operation, body);
            else throw new InvalidDataException($"不支持的弹幕协议版本：{version}。");
            offset += length;
        }
    }

    private static byte[] Pack(Operation operation, byte[] body)
    {
        var packet = new byte[16 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), 1);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8), (int)operation);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12), 1);
        body.CopyTo(packet, 16);
        return packet;
    }
}
