using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Owns the supplied HTTP handler and each WebSocket returned by the connection delegate.</summary>
public sealed class AetherClient(
    HttpMessageHandler httpHandler,
    Func<Uri, CancellationToken, Task<WebSocket>> connectWebSocket,
    TimeProvider timeProvider,
    ILogger<AetherClient> logger,
    string dataDirectory) : IDisposable
{
    private readonly CredentialStore credentials = new(dataDirectory);
    private readonly HttpClient http = new(httpHandler);

    public AetherClient(TimeProvider timeProvider, ILogger<AetherClient> logger)
        : this(new HttpClientHandler { UseCookies = false }, ConnectWebSocketAsync, timeProvider, logger,
            CredentialStore.GetDataDirectory()) { }

    public async IAsyncEnumerable<LoginUpdate> LoginAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var updates = LoginCoreAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasNext;
            try { hasNext = await updates.MoveNextAsync(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { yield break; }
            if (!hasNext) yield break;
            yield return updates.Current;
        }
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        credentials.Delete();
        return Task.CompletedTask;
    }

    private async IAsyncEnumerable<LoginUpdate> LoginCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var login = new BilibiliLogin(http);
        while (true)
        {
            var qr = await login.GenerateAsync(cancellationToken);
            yield return new LoginQrCode(qr.Content);
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
                var (code, refreshToken) = await login.PollAsync(qr.Key, cancellationToken);
                if (code == 86038) break;
                if (code is 86101 or 86090) continue;
                if (code != 0) throw new InvalidOperationException($"扫码登录失败（{code}）。");
                var cookies = await login.GetCookiesAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                credentials.Save(cookies, refreshToken!, timeProvider.GetUtcNow());
                yield return new LoggedIn();
                yield break;
            }
        }
    }

    public async IAsyncEnumerable<WatchUpdate> WatchAsync(
        long roomId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        await using var updates = WatchCoreAsync(roomId, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            bool hasNext;
            try { hasNext = await updates.MoveNextAsync(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { yield break; }
            catch (WebSocketException) when (cancellationToken.IsCancellationRequested) { yield break; }
            if (!hasNext) yield break;
            yield return updates.Current;
        }
    }

    private async IAsyncEnumerable<WatchUpdate> WatchCoreAsync(
        long roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new Connecting();
        var connection = await new BilibiliApi(http, timeProvider).GetConnectionAsync(roomId, cancellationToken);
        using var socket = await connectWebSocket(connection.Server, cancellationToken);
        var authentication = JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = 0, roomid = connection.RoomId, protover = 3, platform = "web", type = 2,
            key = connection.Token, buvid = connection.Buvid
        });
        await socket.SendAsync(DanmakuProtocol.Pack(7, authentication), WebSocketMessageType.Binary, true, cancellationToken);
        using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = Task.CompletedTask;
        try
        {
            var connected = false;
            while (true)
            {
                var bytes = await ReceiveMessageAsync(socket, connectionStop.Token);
                var receivedAt = timeProvider.GetLocalNow();
                foreach (var packet in DanmakuProtocol.Unpack(bytes))
                {
                    if (packet.Operation == 8)
                    {
                        using var auth = JsonDocument.Parse(packet.Body);
                        if (auth.RootElement.GetProperty("code").GetInt32() != 0)
                            throw new InvalidOperationException("弹幕服务器认证失败。");
                        if (!connected)
                        {
                            connected = true;
                            heartbeat = SendHeartbeatsAsync(socket, connectionStop);
                            yield return new Connected();
                        }
                    }
                    else if (packet.Operation == 5 && connected)
                    {
                        using var message = JsonDocument.Parse(packet.Body);
                        var root = message.RootElement;
                        var command = root.GetProperty("cmd").GetString()!;
                        if (command == "DANMU_MSG" || command.StartsWith("DANMU_MSG:", StringComparison.Ordinal))
                        {
                            var info = root.GetProperty("info");
                            yield return new Danmaku(receivedAt, info[2][1].GetString()!, info[1].GetString()!);
                        }
                        else logger.LogDebug("忽略直播间事件 {Command}", command);
                    }
                }
            }
        }
        finally
        {
            await connectionStop.CancelAsync();
            try { await heartbeat; }
            finally
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1), timeProvider);
                    try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", closeTimeout.Token); }
                    catch (Exception error) when (error is WebSocketException or OperationCanceledException)
                    { logger.LogDebug(error, "关闭弹幕连接时传输已不可用"); }
                }
            }
        }
    }

    private async Task SendHeartbeatsAsync(WebSocket socket, CancellationTokenSource stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
                await socket.SendAsync(DanmakuProtocol.Pack(2, []), WebSocketMessageType.Binary, true, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch
        {
            await stop.CancelAsync(); // Wake the receive loop so a send failure cannot leave WatchAsync hanging.
            throw;
        }
    }

    private static async Task<byte[]> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("弹幕服务器已断开连接。");
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("弹幕服务器返回了非二进制消息。");
            if (message.Length + result.Count > DanmakuProtocol.MaxPacketSize)
                throw new InvalidDataException("弹幕数据包过大。");
            message.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return message.ToArray();
    }

    private static async Task<WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0");
        socket.Options.SetRequestHeader("Origin", "https://live.bilibili.com");
        try { await socket.ConnectAsync(uri, cancellationToken); return socket; }
        catch { socket.Dispose(); throw; }
    }

    public void Dispose() => http.Dispose();
}
