using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Aether.Core.Gifts;
using Aether.Core.Entries;

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
    internal sealed record DanmakuReceived(Danmaku Danmaku) : RoomEvent;
    internal sealed record GiftsReceived(Gift[] Gifts, long ReceivedTimestamp) : RoomEvent
    {
        /// <summary>The 礼物榜 期 each gift was counted in, for its 收礼记录 row: the receive loop asks the 礼物榜 before handing
        /// the gifts on; none until then.</summary>
        public Func<Gift, long?> PeriodOf { get; init; } = _ => null;
    }
    internal sealed record EntryReceived(Entry Entry, long ReceivedTimestamp) : RoomEvent;
    /// <summary>A PK message: whether the streamer is in a PK, by its command or a PK_INFO's status, and what else it says that
    /// the 桌宠 plays, none when B站 left that out or wrote it otherwise.</summary>
    internal sealed record PkReceived(bool InPk, long ReceivedTimestamp, PkNews? News) : RoomEvent;

    /// <summary>What a PK message says besides whether the streamer is in a PK; its times are seconds by B站's clock.</summary>
    internal abstract record PkNews;

    /// <summary>PK_BATTLE_PRE_NEW's other streamer.</summary>
    internal sealed record PkMatched(long Uid, long RoomId, string Name) : PkNews;

    /// <summary>PK_BATTLE_START_NEW: when the PK's last 10 seconds start (pk_countdown), and when the message was sent.</summary>
    internal sealed record PkStarted(long Countdown, long Timestamp) : PkNews;

    /// <summary>PK_BATTLE_PROCESS_NEW's or PK_BATTLE_END's votes, init_info's side then match_info's.</summary>
    internal sealed record PkScored(PkSide[] Sides) : PkNews;

    /// <summary>A PK_INFO: the PK's type (pk_basic.type), its members and when it was sent.</summary>
    internal sealed record PkInfo(int Type, PkSide[] Sides, long Timestamp) : PkNews;

    /// <summary>One side of a PK: its room, its votes and, in a PK_INFO, its 胶囊 (capsules_v2).</summary>
    internal sealed record PkSide(long RoomId, long Votes, PkCapsule[] Capsules);

    /// <summary>A 胶囊: its capsule_type, its text without B站's bold marks (&lt;% %&gt;) and its end_time.</summary>
    internal sealed record PkCapsule(int Type, string Text, long EndTime);

    // Whether a PK message's command means the streamer is in a PK, read from its name alone: blivedm knows only the names
    // (handlers.py's logged_unknown_cmds), to which bili_danmu and go-bili-chat add START, START_NEW, SETTLE_NEW and ABNORMAL.
    // A PK starts with its match, PK_BATTLE_PRE_NEW, 10 seconds before it is fought (2026-11-22).
    private static readonly Dictionary<string, bool> PkCommands = new()
    {
        ["PK_BATTLE_PRE_NEW"] = true, ["PK_BATTLE_START"] = true, ["PK_BATTLE_START_NEW"] = true, ["PK_BATTLE_PROCESS"] = true,
        ["PK_BATTLE_PROCESS_NEW"] = true, ["PK_BATTLE_FINAL_PROCESS"] = true,
        ["PK_BATTLE_END"] = false, ["PK_BATTLE_SETTLE"] = false, ["PK_BATTLE_SETTLE_V2"] = false,
        ["PK_BATTLE_SETTLE_NEW"] = false, ["PK_BATTLE_SETTLE_USER"] = false, ["PK_BATTLE_ABNORMAL"] = false
    };

    // A PK_INFO's pk_basic.status while a PK goes on, as the 直播间 page's script names them: 101 waiting for its start, 201
    // fought, 301 its 绝杀. Those after, its end and 惩罚, are no PK.
    private static readonly HashSet<int> PkGoingOn = [101, 201, 301];

    // What the 桌宠 plays of a PK message, by its command.
    private static readonly Dictionary<string, Func<JsonElement, PkNews>> PkNewsOf = new()
    {
        ["PK_BATTLE_PRE_NEW"] = root => Matched(root.GetProperty("data")),
        ["PK_BATTLE_START_NEW"] = root => new PkStarted(root.GetProperty("data").GetProperty("pk_countdown").GetInt64(),
            root.GetProperty("timestamp").GetInt64()),
        ["PK_BATTLE_PROCESS_NEW"] = Scored, ["PK_BATTLE_END"] = Scored,
        ["PK_INFO"] = root => Info(root.GetProperty("data"))
    };

    /// <summary>What one room connection has already logged about B站's message formats; reconnects keep it.</summary>
    private sealed class FormatNotices(ILogger logger, Func<bool> parseEntries)
    {
        private readonly Dictionary<string, string> versions = [];
        private readonly HashSet<string> unknownCommands = [];

        public void Observe(string command)
        {
            if (IsDanmaku(command) || command == "DANMU_MSG_MIRROR") return;
            var (category, version) = command switch
            {
                _ when command.StartsWith("DANMU_MSG", StringComparison.Ordinal) => ("弹幕", null),
                _ when command.StartsWith("SEND_GIFT", StringComparison.Ordinal) => ("礼物", GiftMessages.Version(command)),
                _ when parseEntries() && (command == "INTERACT_WORD" || command.StartsWith("INTERACT_WORD_", StringComparison.Ordinal))
                    => ("进场", EntryMessages.Version(command)),
                _ => (null, null)
            };
            if (category is null) return;
            if (version is not null)
            {
                if (versions.GetValueOrDefault(category) == version) return;
                versions[category] = version;
                logger.LogInformation("本直播间的{Category}消息为 {Version}（{Command}）", category, version, command);
            }
            // B站 moves message kinds to new formats room by room without notice; say so instead of silently missing them.
            // The category appears twice under two names, since structured loggers key properties by name.
            else if (unknownCommands.Add(command))
                logger.LogWarning("收到不认识的{Category}消息 {Command}，{FormatCategory}消息格式可能变了", category, command, category);
        }
    }

    public static byte[] CreateAuthentication(long mid, long roomId, string token, string buvid) =>
        Pack(Operation.Authentication, JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = mid, roomid = roomId, protover = 3, platform = "web", type = 2,
            key = token, buvid
        }));

    public static byte[] CreateHeartbeat() => Pack(Operation.Heartbeat, []);

    /// <summary>One room connection's decoder; reconnects keep its format notices and entry history.</summary>
    internal sealed class Decoder(long roomId, ILogger logger, Func<bool> parseEntries)
    {
        private readonly FormatNotices notices = new(logger, parseEntries);
        // shortcut: keeps every viewer seen this room connection; prune it if a huge room's connection grows too large.
        private readonly Dictionary<long, long> lastEntryTriggers = [];

        public IEnumerable<DecodedEvent> Decode(
            byte[] bytes, DateTimeOffset receivedAt, bool authenticated, long receivedTimestamp)
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
                else if (packet.Operation == Operation.RoomMessage && authenticated)
                    foreach (var decoded in ParseRoomMessage(packet.Body, receivedAt, receivedTimestamp))
                        yield return decoded;
            }
        }

        private DecodedEvent[] ParseRoomMessage(
            ReadOnlyMemory<byte> body, DateTimeOffset receivedAt, long receivedTimestamp)
        {
            string? command = null;
            DecodedEvent[] decoded = [];
            try
            {
                using var message = JsonDocument.Parse(body);
                var root = message.RootElement;
                command = root.GetProperty("cmd").GetString() ?? throw new JsonException("缺少 cmd。");
                notices.Observe(command);
                if (IsDanmaku(command))
                {
                    var info = root.GetProperty("info");
                    var nickname = info[2][1].GetString() ?? throw new JsonException("缺少弹幕昵称。");
                    var content = info[1].GetString() ?? throw new JsonException("缺少弹幕内容。");
                    var (uid, id, replyName) = ReplyDetails(info);
                    var fanClub = ArrayItem(info, 3);
                    var fanClubLevel = NumberAt(fanClub, 0);
                    decoded = [new DanmakuReceived(new Danmaku(receivedAt, nickname, content)
                    {
                        Uid = uid, Id = id, ReplyName = replyName,
                        FanClubLevel = NumberAt(fanClub, 3) == roomId && fanClubLevel > 0 ? fanClubLevel : null,
                        GuardTier = Guard.TierFromPrivilegeType(NumberAt(info, 7)),
                        WealthLevel = NumberAt(ArrayItem(info, 16), 0)
                    })];
                }
                else if (GiftMessages.Decode(command, root, logger) is { Length: > 0 } boxes)
                    decoded = [new GiftsReceived(boxes, receivedTimestamp)];
                else if (parseEntries() && EntryMessages.Decode(command, root, roomId) is { } entry)
                    decoded = [new EntryReceived(entry, receivedTimestamp)];
                else if (Pk(command, root, receivedTimestamp) is { } pk)
                    decoded = [pk];
            }
            // Only message decoding is inside this boundary; frame errors still end the stream.
            catch (Exception error) when (Unreadable(error))
            {
                var length = Math.Min(body.Length, MaxLoggedJsonBytes);
                // Keep a complete UTF-8 prefix when the byte limit lands inside a character.
                while (length < body.Length && length > 0 && (body.Span[length] & 0xC0) == 0x80) length--;
                var json = Encoding.UTF8.GetString(body.Span[..length]);
                logger.LogWarning("跳过无法解析的直播间消息（cmd: {Command}）：{Error}；原始 JSON：{Json}{Truncated}",
                    command, error.Message, json, length < body.Length ? "（已截断，最多 2KB）" : "");
                return [];
            }
            // Valid JSON only has literal CR/LF outside strings; keep each capture on one line.
            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace("直播间消息原始 JSON：{Json}", Encoding.UTF8.GetString(body.Span).Replace("\r", "").Replace("\n", ""));
            if (decoded is [EntryReceived { Entry: var receivedEntry }])
            {
                if (receivedEntry.TriggerTime != 0 && lastEntryTriggers.TryGetValue(receivedEntry.Uid, out var lastTrigger)
                    && lastTrigger == receivedEntry.TriggerTime)
                {
                    logger.LogDebug("忽略同一次进场的重复通知 {Command}，观众 {Uid}", command, receivedEntry.Uid);
                    return [];
                }
                lastEntryTriggers[receivedEntry.Uid] = receivedEntry.TriggerTime;
            }
            if (decoded.Length == 0) logger.LogDebug("忽略直播间事件 {Command}", command);
            return decoded;
        }

        private PkReceived? Pk(string command, JsonElement root, long receivedTimestamp)
        {
            if (command == "PK_INFO")
                return PkGoingOn.Contains(root.GetProperty("data").GetProperty("pk_basic").GetProperty("status").GetInt32())
                    ? new(true, receivedTimestamp, News(command, root)) : null;
            return PkCommands.TryGetValue(command, out var inPk) ? new(inPk, receivedTimestamp, News(command, root)) : null;
        }

        /// <summary>What the 桌宠 plays of a PK message: one with a part B站 left out or wrote otherwise only says whether the
        /// streamer is in a PK.</summary>
        private PkNews? News(string command, JsonElement root)
        {
            if (!PkNewsOf.TryGetValue(command, out var read)) return null;
            try { return read(root); }
            catch (Exception error) when (Unreadable(error))
            {
                logger.LogWarning("PK 消息 {Command} 读不出桌宠要演的内容，只当在 PK 中：{Error}", command, error.Message);
                return null;
            }
        }
    }

    private static bool Unreadable(Exception error) => error is JsonException or KeyNotFoundException
        or InvalidOperationException or IndexOutOfRangeException or FormatException or OverflowException;

    private static PkMatched Matched(JsonElement data) => new(data.GetProperty("uid").GetInt64(), data.GetProperty("room_id").GetInt64(),
        data.GetProperty("uname").GetString() ?? throw new JsonException("缺少对面主播昵称。"));

    private static PkScored Scored(JsonElement root)
    {
        var data = root.GetProperty("data");
        return new([Side(data.GetProperty("init_info")), Side(data.GetProperty("match_info"))]);
    }

    private static PkInfo Info(JsonElement data) => new(data.GetProperty("pk_basic").GetProperty("type").GetInt32(),
        [.. data.GetProperty("members").EnumerateArray().Select(Side)], data.GetProperty("timestamp").GetInt64());

    private static PkSide Side(JsonElement side) => new(side.GetProperty("room_id").GetInt64(), side.GetProperty("votes").GetInt64(),
        side.TryGetProperty("capsules_v2", out var capsules) && capsules.ValueKind == JsonValueKind.Array
            ? [.. capsules.EnumerateArray().Select(Capsule)] : []);

    private static PkCapsule Capsule(JsonElement capsule) => new(capsule.GetProperty("capsule_type").GetInt32(),
        (capsule.GetProperty("text").GetString() ?? throw new JsonException("缺少胶囊文字。")).Replace("<%", "").Replace("%>", ""),
        capsule.GetProperty("end_time").GetInt64());

    /// <summary>The sender uid and danmaku id that only a reply needs, plus the nickname this danmaku itself @s. An
    /// unreadable uid becomes 0 (no reply), an unreadable id "" (a reply without replay_dmid) and an unreadable nickname
    /// "" (no @), so a format change there never hides the danmaku itself.</summary>
    private static (long Uid, string Id, string ReplyName) ReplyDetails(JsonElement info)
    {
        var uid = NumberAt(ArrayItem(info, 2), 0);
        if (info[0] is not { ValueKind: JsonValueKind.Array } meta || meta.GetArrayLength() <= 15
            || meta[15] is not { ValueKind: JsonValueKind.Object } details
            || !details.TryGetProperty("extra", out var extra) || extra.ValueKind != JsonValueKind.String)
            return (uid, "", ""); // Older messages have no extra.
        try
        {
            using var parsed = JsonDocument.Parse(extra.GetString()!);
            if (parsed.RootElement is not { ValueKind: JsonValueKind.Object } root) return (uid, "", "");
            return (uid,
                root.TryGetProperty("id_str", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "",
                root.TryGetProperty("reply_uname", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : "");
        }
        catch (JsonException) { return (uid, "", ""); }
    }

    private static JsonElement ArrayItem(JsonElement array, int index) =>
        array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > index ? array[index] : default;

    private static long NumberAt(JsonElement array, int index) =>
        ArrayItem(array, index) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) && number > 0
            ? number : 0;

    private static bool IsDanmaku(string command) =>
        command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal);

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
