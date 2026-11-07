using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.Gui.Tests.Support;

/// <summary>GitHub's latest-release API and download links for one release whose packages each test builds.</summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string LatestRelease = "https://api.github.test/repos/cyaoc/aether/releases/latest";
    private readonly Dictionary<string, (byte[] Data, string Digest)> assets = [];

    public string Tag { get; set; } = "v0.5.0";
    public List<string> Requests { get; } = [];
    /// <summary>Called as the release is requested; the response waits for the task it returns, e.g. past the check's time limit.</summary>
    public Func<Task>? BeforeRelease { get; set; }
    /// <summary>Set to stop each download after its first <see cref="HeldStream.FirstChunk"/> bytes until the task completes.</summary>
    public Task? DownloadHeld { get; set; }
    /// <summary>Set to answer downloads through gh-proxy.org with this instead of the asset, given the asset's bytes.</summary>
    public Func<byte[], HttpResponseMessage>? Proxy { get; set; }

    public void Add(string name, byte[] data, string? digest = null) =>
        assets[name] = (data, digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(data)));

    public long Size(string name) => assets[name].Data.Length;

    public int Downloads(string name) => Requests.Count(request => request.EndsWith("/" + name));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!.AbsoluteUri;
        lock (Requests) Requests.Add(uri);
        Assert.NotEmpty(request.Headers.UserAgent);
        if (uri == LatestRelease)
        {
            if (BeforeRelease is { } before) await before().WaitAsync(cancellationToken);
            var release = new
            {
                tag_name = Tag,
                assets = assets.Select(asset => new
                {
                    name = asset.Key, size = asset.Value.Data.Length, digest = asset.Value.Digest,
                    browser_download_url = $"https://github.test/cyaoc/aether/releases/download/{Tag}/{asset.Key}"
                })
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) };
        }
        var data = assets[uri[(uri.LastIndexOf('/') + 1)..]].Data;
        if (Proxy is { } proxy && uri.StartsWith("https://gh-proxy.org/")) return proxy(data);
        return new(HttpStatusCode.OK) { Content = new StreamContent(new HeldStream(data, DownloadHeld)) };
    }

    /// <summary>The package as release.yml packs it: every file inside one aether-gui-… directory.</summary>
    public static byte[] Package(string name, IReadOnlyDictionary<string, string> files) =>
        Pack(name, files.ToDictionary(file => $"{Root(name)}/{file.Key}", file => file.Value));

    /// <summary>A delta package as scripts/delta-package.py makes it: changed and new files at the root, and manifest.json.</summary>
    public static byte[] Delta(string name, string fromTag, IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> next)
    {
        var changed = next.Where(file => previous.GetValueOrDefault(file.Key) != file.Value).ToDictionary();
        var manifest = new { from = fromTag, files = next.ToDictionary(file => file.Key, file => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value)))) };
        changed["manifest.json"] = JsonSerializer.Serialize(manifest);
        return Pack(name, changed);
    }

    private static string Root(string name) => name.Replace(".zip", "").Replace(".tar.gz", "");

    private static byte[] Pack(string name, IReadOnlyDictionary<string, string> files)
    {
        using var output = new MemoryStream();
        if (name.EndsWith(".zip"))
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var (path, text) in files)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        else
        {
            using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            using var tar = new TarWriter(gzip);
            foreach (var (path, text) in files)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
                    Mode = Path.GetFileName(path) == "Aether.Gui" ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100
                });
        }
        return output.ToArray();
    }

    /// <summary>Sends the first chunk at once and the rest once <paramref name="held"/> completes.</summary>
    public sealed class HeldStream(byte[] data, Task? held) : MemoryStream(data)
    {
        public const int FirstChunk = 10;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= FirstChunk && held is not null) await held.WaitAsync(cancellationToken);
            return await base.ReadAsync(Position < FirstChunk ? buffer[..(int)Math.Min(buffer.Length, FirstChunk - Position)] : buffer, cancellationToken);
        }
    }
}
