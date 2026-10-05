using System.Net;

namespace Aether.Core.Tests.Support;

internal sealed class FakeBilibiliHttp : HttpMessageHandler
{
    public Dictionary<string, string> Responses { get; } = new();
    public List<(Uri Uri, string? Cookie)> Requests { get; } = [];
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

    /// <summary>Throws <paramref name="error"/> for the first request to <paramref name="path"/>; later requests go to <see cref="Respond"/> as before.</summary>
    public void FailOnce(string path, Exception error)
    {
        var respond = Respond!;
        var failed = false;
        Respond = (request, token) =>
        {
            if (failed || request.RequestUri!.AbsolutePath != path) return respond(request, token);
            failed = true;
            throw error;
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add((uri, request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null));
        if (Respond is not null) return Respond(request, cancellationToken);
        var body = Responses.TryGetValue(uri.GetLeftPart(UriPartial.Path), out var response)
            ? response
            : throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
