using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WebPageResourceTests
{
    [Theory]
    [InlineData("video")]
    [InlineData("lottery")]
    [InlineData("gift-board")]
    public async Task Page_and_its_recursive_local_references_are_served_with_their_content_types(string name)
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        var pending = new Queue<Uri>([new(http.BaseAddress, name)]);
        var visited = new HashSet<Uri>();
        while (pending.TryDequeue(out var uri))
        {
            if (!visited.Add(uri)) continue;
            using var response = await http.GetAsync(uri, h.Stop.Token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{uri}: {response.StatusCode}");
            var expectedType = Path.GetExtension(uri.AbsolutePath) switch
            {
                "" or ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" or ".mjs" => "text/javascript; charset=utf-8",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".webp" => "image/webp",
                _ => throw new InvalidOperationException($"Unexpected page resource: {uri}")
            };
            Assert.Equal(expectedType, response.Content.Headers.ContentType?.ToString());
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(h.Stop.Token));
            if (!expectedType.StartsWith("text/", StringComparison.Ordinal)) continue;
            var text = await response.Content.ReadAsStringAsync(h.Stop.Token);
            foreach (Match match in Regex.Matches(text, """(?:\b(?:href|src)\s*=\s*["']|\burl\(\s*["']?|\bfrom\s*["'])([^"'()\s>]+)"""))
            {
                var reference = match.Groups[1].Value;
                if (reference.StartsWith('#')) continue;
                var asset = new Uri(uri, reference);
                if (asset.GetLeftPart(UriPartial.Authority) == http.BaseAddress.GetLeftPart(UriPartial.Authority)
                    && asset.AbsolutePath.StartsWith($"/{name}/", StringComparison.Ordinal)) pending.Enqueue(asset);
            }
        }
        if (name == "lottery")
        {
            Assert.Equal(28, visited.Count(uri => uri.AbsolutePath.EndsWith(".webp", StringComparison.Ordinal)));
            Assert.Contains(new Uri(http.BaseAddress, "/lottery/SmileySans-Oblique.woff2"), visited);
            Assert.Contains(new Uri(http.BaseAddress, "/lottery/ZCOOLKuaiLe-Regular.ttf"), visited);
        }
        if (name == "gift-board")
        {
            Assert.Equal(3, visited.Count(uri => uri.AbsolutePath.EndsWith(".webp", StringComparison.Ordinal)));
            Assert.Equal(3, visited.Count(uri => uri.AbsolutePath.EndsWith(".woff2", StringComparison.Ordinal)));
            Assert.Equal(12, visited.Count(uri => uri.AbsolutePath.EndsWith(".mjs", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("video")]
    [InlineData("lottery")]
    [InlineData("gift-board")]
    public async Task Explicit_index_returns_the_same_page(string name)
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        var page = await http.GetByteArrayAsync(name, h.Stop.Token);
        using var index = await http.GetAsync($"{name}/index.html", h.Stop.Token);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("text/html; charset=utf-8", index.Content.Headers.ContentType?.ToString());
        Assert.Equal(page, await index.Content.ReadAsByteArrayAsync(h.Stop.Token));
    }

    [Theory]
    [InlineData("lottery/OFL.txt")]
    [InlineData("lottery/ZCOOLKuaiLe-OFL.txt")]
    [InlineData("gift-board/fonts/NotoSansSC-OFL.txt")]
    [InlineData("gift-board/fonts/PlayfairDisplay-OFL.txt")]
    public async Task Font_licenses_are_served_as_plain_text(string path)
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync(path, h.Stop.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("SIL OPEN FONT LICENSE", await response.Content.ReadAsStringAsync(h.Stop.Token));
    }

    [Theory]
    [InlineData("GET", "/video/")]
    [InlineData("GET", "/lottery/")]
    [InlineData("GET", "/video/missing.html")]
    [InlineData("GET", "/lottery/missing.css")]
    [InlineData("GET", "/lottery/README.md")]
    [InlineData("GET", "/gift-board/README.md")]
    [InlineData("GET", "/lottery/page.map")]
    [InlineData("GET", "/unknown/index.html")]
    [InlineData("POST", "/video")]
    [InlineData("HEAD", "/lottery")]
    [InlineData("POST", "/lottery/page.js")]
    [InlineData("HEAD", "/video/index.html")]
    [InlineData("POST", "/lottery/index.html")]
    public async Task Unknown_resources_trailing_slashes_and_non_get_requests_remain_not_found(string method, string path)
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await http.SendAsync(request, h.Stop.Token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/lottery/../../Fixtures/blind-gift-v1.json", 403)]
    [InlineData("/lottery/%2e%2e/%2e%2e/Fixtures/blind-gift-v1.json", 403)]
    [InlineData("/lottery/..%2f..%2fFixtures%2fblind-gift-v1.json", 403)]
    [InlineData("/lottery/..%5c..%5cFixtures%5cblind-gift-v1.json", 403)]
    [InlineData("/lottery/{base}Fixtures/blind-gift-v1.json", 404)]
    [InlineData("/lottery/{base}Fixtures%5cblind-gift-v1.json", 404)]
    public async Task Request_paths_cannot_reach_files_outside_the_pages_directory(string path, int windowsStatus)
    {
        // The raw request line keeps the dot segments that HttpClient would resolve before sending.
        // http.sys answers 403 itself to a path that climbs above the root, before the web service sees it.
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        Assert.NotEmpty(BlindGiftFixture.Json);
        var address = new Uri(h.Client.WebService.Status.Origin);
        var target = path.Replace("{base}", AppContext.BaseDirectory.Replace('\\', '/'), StringComparison.Ordinal);
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(address.Host, address.Port, h.Stop.Token);
        await using var stream = new NetworkStream(socket);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: {address.Authority}\r\nConnection: close\r\n\r\n"), h.Stop.Token);
        using var reader = new StreamReader(stream);
        Assert.StartsWith($"HTTP/1.1 {(OperatingSystem.IsWindows() ? windowsStatus : 404)} ", await reader.ReadLineAsync(h.Stop.Token));
        // HttpListener resolves the dot segments or rejects them before the web service, so the check behind it gets the decoded path too.
        Assert.Null(WebServer.PagesFile(Uri.UnescapeDataString(target)));
    }
}
