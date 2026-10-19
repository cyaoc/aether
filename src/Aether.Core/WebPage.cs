using System.Net;

namespace Aether.Core;

public abstract class WebPage
{
    internal WebPage(string name) => Name = name;
    internal string Name { get; }

    // Streams keep each snapshot ordered with its updates; broadcasts let video count successful deliveries before playing.
    internal virtual IAsyncEnumerable<SseEvent>? EventsAsync(CancellationToken token) => null;
    internal virtual Task<bool> HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token) => Task.FromResult(false);
    internal virtual Task RunAsync(CancellationToken token) => Task.CompletedTask;
    internal virtual void Disconnected() { }
}
