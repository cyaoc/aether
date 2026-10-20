using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Aether.Core;
using Aether.Core.Tests.Support;

namespace Aether.Cli.Tests;

public sealed class WelcomeAnimationBrowserTests
{
    [Fact]
    public async Task Watch_leaves_web_service_stopped_when_welcome_animation_is_off()
    {
        await using var h = new WatchHarness();
        await h.LoginAsync();
        var watching = WatchCommand.RunAsync(h.Client, Settings.Load(h.DataDirectory), 6,
            _ => throw new InvalidOperationException("Already logged in"), h.Stop.Token);
        try
        {
            await h.WaitForLogAsync("已连接弹幕服务器");
            Assert.False(h.Client.WebService.Status.Running);
        }
        finally { await h.Stop.CancelAsync(); await watching; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watch_delivers_an_entry_to_the_production_page_and_Chrome_reports_completed_playback(bool defaultHttpPort)
    {
        var chrome = Environment.GetEnvironmentVariable("AETHER_TEST_CHROME");
        if (string.IsNullOrEmpty(chrome)) Assert.Skip("Set AETHER_TEST_CHROME to a local Chrome executable for browser acceptance.");
        await using var h = new WatchHarness();
        await h.LoginAsync();
        using var availablePort = new TcpListener(Dns.GetHostAddresses("localhost")[0], defaultHttpPort ? 80 : 0) { ExclusiveAddressUse = true };
        try { availablePort.Start(); }
        catch (SocketException error) when (defaultHttpPort && error.SocketErrorCode is SocketError.AccessDenied or SocketError.AddressAlreadyInUse)
        { Assert.Skip("Port 80 is unavailable to this user: " + error.Message); }
        var port = ((IPEndPoint)availablePort.LocalEndpoint).Port;
        availablePort.Stop();
        Settings.Save(h.DataDirectory, new Dictionary<string, string>
        {
            ["welcome_animation.enabled"] = "true", ["web_service.port"] = port.ToString()
        });
        var videos = Path.Combine(h.DataDirectory, "videos");
        Directory.CreateDirectory(videos);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "welcome.mp4"), Path.Combine(videos, "welcome.mp4"));
        Settings.Save(h.DataDirectory, new Dictionary<string, string> { ["welcome_animation_videos.10001"] = "welcome.mp4" });
        var output = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(captured));
        Task watching = Task.CompletedTask;
        Process? browser = null;
        var browserErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        try
        {
            watching = WatchCommand.RunAsync(h.Client, Settings.Load(h.DataDirectory), 6,
                _ => throw new InvalidOperationException("Already logged in"), h.Stop.Token);
            await h.WaitForLogAsync("已连接弹幕服务器");
            await using var statusUpdates = h.Client.WebService.GetUpdatesAsync(h.Stop.Token).GetAsyncEnumerator(h.Stop.Token);
            Assert.True(await statusUpdates.MoveNextAsync());
            var status = Assert.IsType<WebServiceStatus>(statusUpdates.Current);
            Assert.True(status.Running, status.Error);
            var start = new ProcessStartInfo(chrome) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "--headless", "--no-first-run", "--no-default-browser-check",
                "--autoplay-policy=no-user-gesture-required", "--user-data-dir=" + Path.Combine(h.DataDirectory, "chrome"), h.Client.WebService.Status.VideoAddress })
                start.ArgumentList.Add(argument);
            browser = Process.Start(start)!;
            browser.ErrorDataReceived += (_, e) => { if (e.Data is { } line) browserErrors.Enqueue(line); };
            browser.BeginErrorReadLine();
            browser.BeginOutputReadLine();
            try { await h.WaitForLogAsync("视频页 1 个推流软件"); }
            catch (OperationCanceledException error)
            {
                throw new InvalidOperationException("Chrome did not connect: " + string.Join("\n", browserErrors), error);
            }
            await h.Server.PushRoomMessageAsync(EntryV2Fixture.Message());
            await h.WaitForLogAsync("已播放");
            Assert.Contains($"视频页：http://localhost:{port}/video", captured.ToString());
            Assert.DoesNotContain(h.Logger.Entries, entry => entry.Message.Contains("播放失败"));
            Assert.Contains(h.Logger.Entries, entry => entry.Message.Contains("试播=False") && entry.Message.Contains("已播放"));
        }
        finally
        {
            await h.Stop.CancelAsync();
            await watching;
            Console.SetOut(output);
            if (browser is not null)
            {
                if (!browser.HasExited) browser.Kill(entireProcessTree: true);
                await browser.WaitForExitAsync(TestContext.Current.CancellationToken);
                browser.Dispose();
            }
        }
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"http://localhost:{port}/", TestContext.Current.CancellationToken));
    }
}
