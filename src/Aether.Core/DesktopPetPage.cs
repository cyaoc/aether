namespace Aether.Core;

/// <summary>The 桌宠页: it plays what <see cref="DesktopPet"/> tells it.</summary>
internal sealed class DesktopPetPage(WebServer server) : WebPage("desktop-pet")
{
    internal override IAsyncEnumerable<SseEvent> EventsAsync(CancellationToken token) =>
        server.Region.SubscribeAsync(s => s.DesktopPet.Subscribers, s => s.DesktopPet.State, token);
}
