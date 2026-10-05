using System.Buffers.Binary;
using System.IO.Compression;

namespace Aether.Core;

internal static class DanmakuProtocol
{
    public const int MaxPacketSize = 8 * 1024 * 1024;

    public static IEnumerable<(int Operation, ReadOnlyMemory<byte> Body)> Unpack(byte[] bytes, int depth = 0)
    {
        if (depth > 4) throw new InvalidDataException("弹幕压缩包嵌套过深。");
        for (var offset = 0; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 16) throw new InvalidDataException("弹幕包头不完整。");
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            var headerLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 4));
            if (headerLength < 16 || length < headerLength || length > bytes.Length - offset)
                throw new InvalidDataException("弹幕包长度无效。");
            var operation = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 8));
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

    public static byte[] Pack(int operation, byte[] body)
    {
        var packet = new byte[16 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), 1);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8), operation);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12), 1);
        body.CopyTo(packet, 16);
        return packet;
    }
}
