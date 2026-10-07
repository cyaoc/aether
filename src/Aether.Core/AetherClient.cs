using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Core;

/// <summary>Owns the supplied HTTP handler and each WebSocket returned by the connection delegate.</summary>
public sealed partial class AetherClient(
    HttpMessageHandler httpHandler,
    Func<Uri, CancellationToken, Task<WebSocket>> connectWebSocket,
    TimeProvider timeProvider,
    ILogger<AetherClient> logger,
    string dataDirectory) : IDisposable
{
    private readonly HttpClient http = new(httpHandler);
    // Fields rather than captured parameters, so the nested RoomConnection can reach them.
    private readonly Func<Uri, CancellationToken, Task<WebSocket>> connectWebSocket = connectWebSocket;
    private readonly TimeProvider timeProvider = timeProvider;
    private readonly ILogger<AetherClient> logger = logger;
    private readonly string dataDirectory = dataDirectory;
    // PublicationOnly does not cache a failed open, so a transient database error does not poison a long-lived client.
    private readonly Lazy<CredentialStore> credentialStore = new(
        () => new CredentialStore(new Database(dataDirectory)), LazyThreadSafetyMode.PublicationOnly);

    private static readonly TimeSpan CredentialCheckInterval = TimeSpan.FromHours(24);

    // BilibiliApi keeps cookies per flow so a fresh login cannot inherit another flow's credential.
    public AetherClient(ILogger<AetherClient> logger, string dataDirectory)
        : this(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All },
            ConnectWebSocketAsync, TimeProvider.System, logger,
            dataDirectory) { }

    public IAsyncEnumerable<LoginUpdate> LoginAsync(CancellationToken cancellationToken = default) =>
        EndOnCancellation(LoginCoreAsync(cancellationToken), cancellationToken);

    /// <summary>Signs out on B站 when possible; the local credential is deleted either way unless the caller cancels.</summary>
    /// <remarks>Takes no room lock. A room connection running elsewhere on this data directory keeps going,
    /// and its next reconnect asks for a scan.</remarks>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var credentials = credentialStore.Value;
        try
        {
            if (credentials.Load() is { } credential)
            {
                var api = new BilibiliApi(http, timeProvider, credential);
                await api.LogoutAsync(cancellationToken);
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("B站退出登录失败，仍删除本地登录凭据：{Error}", error.Message);
        }
        credentials.Delete();
    }

    public IAsyncEnumerable<WatchUpdate> WatchAsync(long roomId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomId);
        return EndOnCancellation(RunRoomConnectionAsync(roomId, cancellationToken), cancellationToken);
    }

    /// <summary>Core runs the room connection on its own; the caller only reads what it reports,
    /// so a slow reader never holds up receiving, heartbeats or the idle deadline.</summary>
    private async IAsyncEnumerable<WatchUpdate> RunRoomConnectionAsync(
        long roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ponytail: unbounded, so a reader that stops for good grows memory; bound it if a stalled shell ever matters.
        var updates = Channel.CreateUnbounded<WatchUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Started inline, so it runs on the caller's first read up to its first real wait; but without the caller's
        // SynchronizationContext, so everything after that runs on the thread pool instead of a shell's UI thread.
        var shellContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        Task running;
        try { running = RunAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(shellContext); }
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken))
                yield return update;
            await running; // Ended on its own: report how.
        }
        finally
        {
            if (!running.IsCompleted)
            {
                await stop.CancelAsync();
                // Wait, so the room lock is free once the caller's DisposeAsync returns. The caller has stopped reading,
                // and RoomConnection already logged any real failure, so there is nobody left to report it to.
                try { await running; }
                catch { }
            }
        }

        async Task RunAsync()
        {
            try
            {
                var settings = Settings.Load(dataDirectory, logger);
                if (settings.Error is { } settingsError) throw settingsError;
                await new RoomConnection(this, roomId, settings, updates.Writer).RunAsync(stop.Token);
            }
            finally { updates.Writer.Complete(); }
        }
    }

    /// <summary>Ends the stream normally, instead of throwing, once the caller cancels.</summary>
    private static async IAsyncEnumerable<T> EndOnCancellation<T>(
        IAsyncEnumerable<T> source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var updates = source.GetAsyncEnumerator(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool hasNext;
            try { hasNext = await updates.MoveNextAsync(); }
            catch (Exception error) when (cancellationToken.IsCancellationRequested
                && error is OperationCanceledException or WebSocketException) { yield break; }
            if (!hasNext) yield break;
            yield return updates.Current;
        }
    }

    private async IAsyncEnumerable<LoginUpdate> LoginCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken, bool acceptExternalCredential = false,
        string? previousToken = null)
    {
        // Everything that can fail on its own runs before the first QR code, so no scan is wasted.
        var credentials = credentialStore.Value;
        var api = new BilibiliApi(http, timeProvider, credential: null);
        while (true)
        {
            var qr = await RetryAsync(() => api.GenerateQrCodeAsync(cancellationToken), cancellationToken);
            // Armed before the QR code is reported, so a reader that sees it knows the first poll's wait has begun.
            var poll = Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
            yield return new LoginQrCode(qr.Url);
            while (true)
            {
                await poll;
                poll = Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
                if (acceptExternalCredential && credentials.Load() is { } external && external.RefreshToken != previousToken)
                {
                    yield return new LoggedIn(); // The watch flow validates it with nav; do not save over another process.
                    yield break;
                }
                // A failed poll goes round the loop, so the retry still checks for an external credential first.
                if (await TryLoginRequestAsync(() => api.PollQrCodeAsync(qr.Key, cancellationToken), cancellationToken)
                    is not (var state, var credential)) continue;
                if (state == QrCodeState.Waiting) continue;
                if (state == QrCodeState.Expired) break;
                cancellationToken.ThrowIfCancellationRequested();
                credentials.Save(credential!);
                yield return new LoggedIn();
                yield break;
            }
        }
    }

    /// <summary>Only a failed connection is worth retrying, and only while the caller still wants the result. Once B站 answers
    /// with a refusal (an error code, HTTP 4xx, rejected authentication) the same request gets the same answer and hammering it
    /// deepens risk control; local errors and bugs never heal.</summary>
    private static bool IsRetryable(Exception error, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && error
            is HttpRequestException { StatusCode: null or >= HttpStatusCode.InternalServerError }
            or WebSocketException or OperationCanceledException;

    /// <summary>Retries <see cref="IsRetryable"/> failures until the request succeeds or the caller cancels.</summary>
    private async Task<T> RetryAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken) where T : struct
    {
        while (true)
        {
            if (await TryLoginRequestAsync(request, cancellationToken) is { } result) return result;
            // ponytail: fixed 2s retry with no cap; add backoff if B站 starts rate-limiting retries.
            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, cancellationToken);
        }
    }

    /// <summary>One login request; an <see cref="IsRetryable"/> failure is logged and returns null, leaving the retry to the caller.</summary>
    private async Task<T?> TryLoginRequestAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken) where T : struct
    {
        try { return await request(); }
        catch (Exception error) when (IsRetryable(error, cancellationToken))
        {
            logger.LogWarning("登录请求失败，2 秒后重试：{Error}", error.Message);
            return null;
        }
    }

    /// <param name="refreshCancellation">Abandons a refresh B站 may already have issued, dropping the new credential unsaved;
    /// so only the caller cancels it, never the end of a room connection.</param>
    private async Task<Credential?> RefreshCredentialIfDueAsync(
        CancellationToken cancellationToken, CancellationToken refreshCancellation)
    {
        var credentials = credentialStore.Value;
        var credential = credentials.Load();
        if (credential is null || credential.CheckedAt is { } checkedAt
            && timeProvider.GetUtcNow() - checkedAt < CredentialCheckInterval) return credential;
        try
        {
            var api = new BilibiliApi(http, timeProvider, credential);
            if (await api.GetRefreshTimestampAsync(cancellationToken) is { } timestamp)
            {
                var refreshed = await api.RefreshCredentialAsync(timestamp, refreshCancellation);
                if (credentials.Save(refreshed, credential.RefreshToken))
                {
                    try
                    {
                        await new BilibiliApi(http, timeProvider, refreshed)
                            .ConfirmCredentialRefreshAsync(credential.RefreshToken, cancellationToken);
                    }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning("确认登录凭据刷新失败，保留新登录凭据：{Error}", error.Message);
                    }
                }
            }
            else
                credentials.MarkChecked(credential.RefreshToken, timeProvider.GetUtcNow());
        }
        catch (Exception error) when (IsRetryable(error, cancellationToken))
        {
            logger.LogWarning("检查或刷新登录凭据时网络失败，下次再试：{Error}", error.Message);
        }
        catch (CredentialRejectedException error)
        {
            if (credentials.Delete(credential.RefreshToken))
                logger.LogWarning("{Error} 请重新扫码登录。", error.Message);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // Refresh is upkeep, and nav still vets the credential on every connect, so an unexpected answer
            // (an error code from cookie/info, a changed correspond page) neither deletes it nor ends the room connection.
            logger.LogWarning("检查或刷新登录凭据失败，24 小时后再试：{Error}", error.Message);
            credentials.MarkChecked(credential.RefreshToken, timeProvider.GetUtcNow());
        }
        return credentials.Load();
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

public static class AetherHosting
{
    /// <summary>What both shells host: Core's file logs and one client on the data directory.
    /// A settings error does not stop startup; Core refuses the next room connection with it.</summary>
    public static IHostApplicationBuilder AddAether(this IHostApplicationBuilder builder, string dataDirectory)
    {
        builder.Logging.AddAetherFileLogging(dataDirectory);
        builder.Services.AddSingleton(services => new AetherClient(
            services.GetRequiredService<ILogger<AetherClient>>(), dataDirectory));
        return builder;
    }
}
