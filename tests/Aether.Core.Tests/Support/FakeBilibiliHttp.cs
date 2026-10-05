using System.Net;

namespace Aether.Core.Tests.Support;

internal sealed class FakeBilibiliHttp : HttpMessageHandler
{
    public Dictionary<string, string> Responses { get; } = new();
    public List<(Uri Uri, string? Cookie)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add((uri, request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null));
        var body = Responses.TryGetValue(uri.GetLeftPart(UriPartial.Path), out var response)
            ? response
            : throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
