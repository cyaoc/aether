using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

internal static class DanmakuProtocol
{
    public const int MaxPacketSize = 8 * 1024 * 1024;

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

    public static byte[] CreateAuthentication(long mid, long roomId, string token, string buvid) =>
        Pack(Operation.Authentication, JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = mid, roomid = roomId, protover = 3, platform = "web", type = 2,
            key = token, buvid
        }));

    public static byte[] CreateHeartbeat() => Pack(Operation.Heartbeat, []);

    public static IEnumerable<DecodedEvent> Decode(
        byte[] bytes, DateTimeOffset receivedAt, bool authenticated, ILogger logger)
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
                && ParseRoomMessage(packet.Body, receivedAt, logger) is { } danmaku)
                yield return new DanmakuReceived(danmaku);
        }
    }

    private static Danmaku? ParseRoomMessage(ReadOnlyMemory<byte> body, DateTimeOffset receivedAt, ILogger logger)
    {
        string? command = null;
        try
        {
            using var message = JsonDocument.Parse(body);
            var root = message.RootElement;
            command = root.GetProperty("cmd").GetString() ?? throw new JsonException("缺少 cmd。");
            if (command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal))
            {
                var info = root.GetProperty("info");
                var nickname = info[2][1].GetString() ?? throw new JsonException("缺少弹幕昵称。");
                var content = info[1].GetString() ?? throw new JsonException("缺少弹幕内容。");
                return new Danmaku(receivedAt, nickname, content);
            }
        }
        // Only JSON parsing and field access are inside this boundary; frame errors still end the stream.
        catch (Exception error) when (error is JsonException or KeyNotFoundException
            or InvalidOperationException or IndexOutOfRangeException)
        {
            var length = Math.Min(body.Length, 2048);
            // Keep a complete UTF-8 prefix when the byte limit lands inside a character.
            while (length < body.Length && length > 0 && (body.Span[length] & 0xC0) == 0x80) length--;
            var json = Encoding.UTF8.GetString(body.Span[..length]);
            logger.LogWarning("跳过无法解析的直播间消息（cmd: {Command}）：{Error}；原始 JSON：{Json}{Truncated}",
                command, error.Message, json, body.Length > 2048 ? "（已截断，最多 2KB）" : "");
            return null;
        }
        logger.LogDebug("忽略直播间事件 {Command}", command);
        return null;
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
