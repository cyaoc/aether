using System.Net;

namespace Aether.Core;

public abstract class WebPage
{
    internal WebPage(string name) => Name = name;
    internal string Name { get; }

    // Streams keep each snapshot ordered with its updates; broadcasts let video count successful deliveries before playing.
    internal virtual IAsyncEnumerable<SseEvent>? EventsAsync(CancellationToken token) => null;
    // The server serving the page hands itself to the page's request handling and to what runs while it serves.
    internal virtual Task<bool> HandleAsync(WebServer server, HttpListenerRequest request, HttpListenerResponse response, CancellationToken token) => Task.FromResult(false);
    internal virtual Task RunAsync(WebServer server, CancellationToken token) => Task.CompletedTask;
}
