namespace Aether.Gui.Tests;

public sealed class LatestRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_or_cancelled_run_does_not_block_stop_or_the_next_run(bool cancelled)
    {
        var runs = new LatestRun();
        var failed = runs.Start(_ => cancelled
            ? Task.FromCanceled(new CancellationToken(true))
            : Task.FromException(new InvalidOperationException("failed")));
        Assert.NotNull(await Record.ExceptionAsync(() => failed));
        await runs.StopAsync();
        var ran = false;
        await runs.Start(_ => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
        await runs.StopAsync();
    }

    [Fact]
    public async Task Replacement_waits_for_the_cancelled_run_even_when_it_fails()
    {
        var runs = new LatestRun();
        var finished = new TaskCompletionSource();
        CancellationToken previous = default;
        var first = runs.Start(token => { previous = token; return finished.Task; });
        var ran = false;
        var second = runs.Start(_ => { ran = true; return Task.CompletedTask; });
        Assert.True(previous.IsCancellationRequested);
        Assert.False(ran);
        finished.SetException(new InvalidOperationException("failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;
        Assert.True(ran);
        await runs.StopAsync();
    }
}
