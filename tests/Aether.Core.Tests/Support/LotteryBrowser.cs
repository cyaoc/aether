using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace Aether.Core.Tests.Support;

/// <summary>Optional real-browser acceptance, using Chrome's bundled DevTools protocol without a test dependency.</summary>
internal sealed class LotteryBrowser : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private Process? process;
    private string? session;
    private int nextId;
    public List<string> Requests { get; } = [];
    public List<string> Errors { get; } = [];

    public async Task OpenAsync(string directory, string url, int width = 1920, int height = 1080)
    {
        var executable = Environment.GetEnvironmentVariable("AETHER_TEST_CHROME");
        if (string.IsNullOrEmpty(executable)) Assert.Skip("Set AETHER_TEST_CHROME for real lottery browser acceptance.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--headless", "--no-first-run", "--no-default-browser-check", "--remote-debugging-port=0",
            "--user-data-dir=" + Path.Combine(directory, "chrome"), "about:blank" }) start.ArgumentList.Add(arg);
        var endpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        process = new Process { StartInfo = start };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("DevTools listening on ") == true) endpoint.TrySetResult(e.Data[22..]);
        };
        process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine();
        socket.Options.Proxy = null;
        await socket.ConnectAsync(new(await endpoint.Task.WaitAsync(TimeSpan.FromSeconds(15))), TestContext.Current.CancellationToken);
        var target = await CommandAsync("Target.createTarget", new { url = "about:blank" });
        var attached = await CommandAsync("Target.attachToTarget", new { targetId = target.GetProperty("targetId").GetString(), flatten = true });
        session = attached.GetProperty("sessionId").GetString();
        await CommandAsync("Page.enable");
        await CommandAsync("Runtime.enable");
        await CommandAsync("Network.enable");
        await CommandAsync("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });
        await CommandAsync("Emulation.setDefaultBackgroundColorOverride", new { color = new { r = 0, g = 0, b = 0, a = 0 } });
        await CommandAsync("Page.navigate", new { url });
        await WaitAsync("document.readyState === 'complete' && document.getElementById('scene') && document.fonts.status === 'loaded'");
    }

    public async Task<JsonElement> CommandAsync(string method, object? parameters = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var id = ++nextId;
        var command = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new { } };
        if (session is not null) command["sessionId"] = session;
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(command).AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[65536];
        while (true)
        {
            using var message = new MemoryStream();
            ValueWebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Chrome disconnected.");
                message.Write(buffer, 0, part.Count);
            } while (!part.EndOfMessage);
            using var json = JsonDocument.Parse(message.ToArray());
            var root = json.RootElement;
            if (root.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
            {
                Assert.False(root.TryGetProperty("error", out _), root.ToString());
                return root.GetProperty("result").Clone();
            }
            if (root.TryGetProperty("method", out var name))
            {
                if (name.GetString() == "Network.requestWillBeSent") Requests.Add(root.GetProperty("params").GetProperty("request").GetProperty("url").GetString()!);
                if (name.GetString() == "Runtime.exceptionThrown") Errors.Add(root.ToString());
            }
        }
    }

    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        var result = await CommandAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true });
        Assert.False(result.TryGetProperty("exceptionDetails", out _), result.ToString());
        return result.GetProperty("result").GetProperty("value");
    }

    public async Task WaitAsync(string expression, int seconds = 10)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if ((await EvaluateAsync("Boolean(" + expression + ")")).GetBoolean()) return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Fail("Browser did not reach: " + expression);
    }

    public async Task ScreenshotAsync(string name)
    {
        if (Environment.GetEnvironmentVariable("AETHER_TEST_SCREENSHOTS") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var theme = (await EvaluateAsync("document.getElementById('scene').dataset.theme")).GetString();
        var width = (await EvaluateAsync("window.innerWidth")).GetInt32();
        var result = await CommandAsync("Page.captureScreenshot", new { format = "png" });
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{theme}-{width}-{name}.png"), Convert.FromBase64String(result.GetProperty("data").GetString()!), TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        socket.Dispose();
        if (process is null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None);
        process.Dispose();
    }
}
