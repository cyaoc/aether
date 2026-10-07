using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace Aether.Core.Tests.Support;

// A real BCL WebSocket pair over loopback; no Bilibili access or HTTP listener required.
internal sealed class FakeDanmakuServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim sendLock = new(1);
    private readonly Channel<byte[]> received = Channel.CreateUnbounded<byte[]>();
    private WebSocket? server;
    private Task? receiveTask;
    public Uri? ConnectedUri { get; private set; }
    public bool Disconnected { get; private set; }
    public int AuthenticationCode { get; set; }
    public byte[]? AuthenticationReply { get; set; }
    public bool FragmentAuthentication { get; set; }
    public bool ReplyToHeartbeats { get; set; } = true;
    public bool ReplyToAuthentication { get; set; } = true;
    public Task WaitForDisconnectAsync() => receiveTask!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedUri = uri;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);
        var peer = await listener.AcceptTcpClientAsync(cancellationToken);
        server = WebSocket.CreateFromStream(peer.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        var socket = WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan);
        receiveTask = ReceiveAsync();
        return socket;
    }

    public async Task<byte[]> NextRequestAsync() =>
        await received.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    // Waits instead of peeking, so a request still in flight on loopback is not missed. Does not consume it.
    public async Task<bool> ReceivesRequestWithinAsync(TimeSpan timeout)
    {
        try { return await received.Reader.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(timeout); }
        catch (TimeoutException) { return false; }
    }

    public async Task PushAsync(byte[] bytes, bool endOfMessage = true)
    {
        await sendLock.WaitAsync(stop.Token);
        try { await server!.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage, stop.Token); }
        finally { sendLock.Release(); }
    }

    public async Task DisconnectAsync() =>
        await server!.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test disconnect", stop.Token);

    private async Task ReceiveAsync()
    {
        try
        {
            var buffer = new byte[65536];
            while (!stop.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await server!.ReceiveAsync(buffer, stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                var packet = message.ToArray();
                // Core may reconnect here after an earlier connection ended and completed the channel; that connection's
                // requests go unrecorded, but the server still answers them instead of failing.
                received.Writer.TryWrite(packet);
                switch (BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(8)))
                {
                    case 7 when ReplyToAuthentication:
                        var auth = !IsValidAuthentication(packet) ? AuthenticationPacket(-101)
                            : AuthenticationReply ?? AuthenticationPacket(AuthenticationCode);
                        if (FragmentAuthentication)
                        {
                            await PushAsync(auth[..5], false);
                            await PushAsync(auth[5..]);
                        }
                        else await PushAsync(auth);
                        break;
                    case 2 when ReplyToHeartbeats:
                        await PushAsync(Packet(3, [0, 0, 0, 1]));
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (WebSocketException) { } // Client cancellation aborts an in-flight receive.
        finally { Disconnected = true; received.Writer.TryComplete(); }
    }

    private static byte[] AuthenticationPacket(int code) =>
        Packet(8, System.Text.Encoding.UTF8.GetBytes($"{{\"code\":{code}}}"));

    private static bool IsValidAuthentication(byte[] packet)
    {
        try
        {
            using var auth = JsonDocument.Parse(packet.AsMemory(16));
            var root = auth.RootElement;
            return BinaryPrimitives.ReadInt32BigEndian(packet) == packet.Length
                && BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)) == 16
                && root.GetProperty("uid").GetInt64() >= 0
                && root.GetProperty("roomid").GetInt64() > 0
                && root.GetProperty("protover").GetInt32() == 3
                && root.GetProperty("platform").GetString() == "web"
                && root.GetProperty("type").GetInt32() == 2
                && !string.IsNullOrEmpty(root.GetProperty("key").GetString())
                && !string.IsNullOrEmpty(root.GetProperty("buvid").GetString());
        }
        catch { return false; } // Malformed JSON, missing fields and wrong value kinds are all invalid.
    }

    public static byte[] Packet(int operation, byte[] body, ushort version = 1)
    {
        var packet = new byte[16 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6), version);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8), operation);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12), 1);
        body.CopyTo(packet, 16);
        return packet;
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        server?.Dispose();
        if (receiveTask is not null) await receiveTask;
        stop.Dispose();
        sendLock.Dispose();
    }
}
