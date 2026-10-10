namespace Aether.Core.Tests.Support;

internal static class Eventually
{
    /// <summary>Waits up to 5 seconds for background work to make <paramref name="condition"/> true.</summary>
    public static async Task TrueAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
