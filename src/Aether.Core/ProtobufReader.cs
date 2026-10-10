using System.Text;

namespace Aether.Core;

/// <summary>Only protobuf wire framing; field numbers and message meaning belong to the caller.</summary>
// ponytail: reads what B站's gift messages use (plain varints, strings, nested messages, skipped fields); zigzag sint and
// packed repeated fields would read wrong values, so add readers for them when a message first needs one.
internal sealed class ProtobufReader(ReadOnlyMemory<byte> data)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private ReadOnlyMemory<byte> remaining = data;

    public bool TryRead(out int field, out int wireType)
    {
        field = wireType = 0;
        if (remaining.IsEmpty) return false;
        var tag = Varint();
        if (tag > uint.MaxValue || tag >> 3 == 0) throw new FormatException("protobuf 字段号无效。");
        field = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        return true;
    }

    public long Int64(int wireType)
    {
        Require(wireType, 0);
        return unchecked((long)Varint());
    }

    public ReadOnlyMemory<byte> Bytes(int wireType)
    {
        Require(wireType, 2);
        var length = Varint();
        if (length > (ulong)remaining.Length) throw new FormatException("protobuf 字段长度超出消息。");
        return Take((int)length);
    }

    public string Text(int wireType)
    {
        try { return Utf8.GetString(Bytes(wireType).Span); }
        catch (DecoderFallbackException error) { throw new FormatException("protobuf 字符串不是有效 UTF-8。", error); }
    }

    public void Skip(int field, int wireType, int depth = 0)
    {
        switch (wireType)
        {
            case 0: Varint(); break;
            case 1: Take(8); break;
            case 2: Bytes(wireType); break;
            case 3:
                if (depth >= 64) throw new FormatException("protobuf group 嵌套过深。");
                while (TryRead(out var child, out var type))
                {
                    if (type == 4)
                    {
                        if (child != field) throw new FormatException("protobuf group 结束字段不匹配。");
                        return;
                    }
                    Skip(child, type, depth + 1);
                }
                throw new FormatException("protobuf group 未结束。");
            case 5: Take(4); break;
            default: throw new FormatException("protobuf wire type 无效。");
        }
    }

    private ulong Varint()
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var next = Take(1).Span[0];
            if (shift == 63 && next > 1) throw new FormatException("protobuf varint 超出 64 位。");
            value |= (ulong)(next & 127) << shift;
            if (next < 128) return value;
        }
        throw new FormatException("protobuf varint 未结束。");
    }

    private ReadOnlyMemory<byte> Take(int count)
    {
        if (count > remaining.Length) throw new FormatException("protobuf 字段不完整。");
        var result = remaining[..count];
        remaining = remaining[count..];
        return result;
    }

    private static void Require(int actual, int expected)
    {
        if (actual != expected) throw new FormatException("protobuf 字段的 wire type 不匹配。");
    }
}
