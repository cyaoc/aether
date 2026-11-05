namespace Aether.Core.Tests.Support;

internal static class Eventually
{
    /// <summary>Waits up to 5 seconds for background work to make <paramref name="condition"/> true.</summary>
    public static Task TrueAsync(Func<bool> condition) => TrueAsync(() => Task.FromResult(condition()));

    /// <summary>The same for a condition that has to ask something, such as a browser, to tell.</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }
}
