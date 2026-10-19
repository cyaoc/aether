using System.Net;
using System.Text.RegularExpressions;
using Aether.Core.Tests.Support;

namespace Aether.Core.Tests;

public sealed class WebPageResourceTests
{
    [Theory]
    [InlineData("video")]
    [InlineData("lottery")]
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
                ".js" => "text/javascript; charset=utf-8",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                _ => throw new InvalidOperationException($"Unexpected page resource: {uri}")
            };
            Assert.Equal(expectedType, response.Content.Headers.ContentType?.ToString());
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(h.Stop.Token));
            if (!expectedType.StartsWith("text/", StringComparison.Ordinal)) continue;
            var text = await response.Content.ReadAsStringAsync(h.Stop.Token);
            foreach (Match match in Regex.Matches(text, """(?:\b(?:href|src)\s*=\s*["']|\burl\(\s*["']?)([^"'()\s>]+)"""))
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
            Assert.Contains(new Uri(http.BaseAddress, "/lottery/SmileySans-Oblique.woff2"), visited);
            Assert.Contains(new Uri(http.BaseAddress, "/lottery/ZCOOLKuaiLe-Regular.ttf"), visited);
        }
    }

    [Theory]
    [InlineData("video")]
    [InlineData("lottery")]
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
    [InlineData("OFL.txt")]
    [InlineData("ZCOOLKuaiLe-OFL.txt")]
    public async Task Font_licenses_are_served_as_plain_text(string file)
    {
        await using var h = new WatchHarness();
        await h.Client.WebService.StartAsync(WelcomeAnimationTests.FreePort());
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(h.Client.WebService.Status.Origin) };
        using var response = await http.GetAsync($"lottery/{file}", h.Stop.Token);
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
}
