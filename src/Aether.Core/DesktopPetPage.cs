using System.Net;

namespace Aether.Core;

/// <summary>The 桌宠页: it plays what <see cref="DesktopPet"/> tells it, and reports a 试演 that is over, as the video page reports
/// what it played. While the page is served, the lottery's and the 礼物榜's own updates reach the 桌宠 from here, each in a
/// step.</summary>
internal sealed class DesktopPetPage(WebServer server, LotteryFacade lottery, GiftBoardFacade giftBoard) : WebPage("desktop-pet")
{
    internal override IAsyncEnumerable<SseEvent> EventsAsync(CancellationToken token) =>
        server.Region.SubscribeAsync(s => s.DesktopPet.Subscribers, s => s.DesktopPet.State, token);

    internal override Task RunAsync(CancellationToken token) => Task.WhenAll(
        ForwardAsync(lottery.GetUpdatesAsync(token), (s, update) => s.DesktopPet.SetLottery(update), token),
        ForwardAsync(giftBoard.GetUpdatesAsync(token), (s, state) => s.DesktopPet.SetGiftBoard(state), token));

    internal override Task<bool> HandleAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken token)
    {
        if (request.HttpMethod != "POST" || request.Url!.AbsolutePath != "/desktop-pet/report") return Task.FromResult(false);
        if (!server.FromItsPage(request)) response.StatusCode = 403;
        else
        {
            server.Region.Post(s => s.DesktopPet.TestShowEnded());
            response.StatusCode = 204;
        }
        return Task.FromResult(true);
    }

    private async Task ForwardAsync<T>(IAsyncEnumerable<T> updates, Action<WebService, T> step, CancellationToken token)
    {
        try
        {
            await foreach (var update in updates) server.Region.Post(s => step(s, update));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
