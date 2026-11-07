using Microsoft.Extensions.Time.Testing;

namespace Aether.Core.Tests;

/// <summary>ADR 0018: one process per data/, through data/aether.lock. Each lock stands in for a process holding it.</summary>
public sealed class DataDirectoryLockTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aether-tests-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new();
    private string LockFile => Path.Combine(directory, "aether.lock");

    [Fact]
    public async Task Concurrent_attempts_keep_the_data_directory_exclusive()
    {
        // Every read moves this clock past the wait, so an attempt that finds the lock held gives up at once.
        var impatient = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromMinutes(1) };
        var owners = 0;
        var accepted = 0;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenders = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (var i = 0; i < 300; i++)
            {
                await using var held = await DataDirectory.LockAsync(directory, impatient);
                if (held is null) continue;
                Assert.Equal(1, Interlocked.Increment(ref owners));
                Interlocked.Increment(ref accepted);
                await Task.Yield();
                Interlocked.Decrement(ref owners);
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        start.SetResult();
        await Task.WhenAll(contenders).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.True(accepted > 1);
    }

    [Fact]
    public async Task A_lock_let_go_within_five_seconds_is_taken_over()
    {
        var old = await DataDirectory.LockAsync(directory, time);
        Assert.NotNull(old);
        var waiting = DataDirectory.LockAsync(directory, time);
        Assert.False(waiting.IsCompleted);

        // The old version exits while the update has already started the new one.
        await old.DisposeAsync();
        time.Advance(TimeSpan.FromSeconds(4));

        await using var taken = await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task A_lock_still_held_after_five_seconds_is_not_taken_and_its_file_stays()
    {
        await using var owner = await DataDirectory.LockAsync(directory, time);
        var waiting = DataDirectory.LockAsync(directory, time);
        Assert.False(waiting.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        // The lock deletes itself on close; a refused open must not take the owner's file with it.
        Assert.True(File.Exists(LockFile));
    }

    [Fact]
    public async Task Letting_go_deletes_the_lock_file_and_the_next_one_takes_it_at_once()
    {
        await using (await DataDirectory.LockAsync(directory, time)) Assert.True(File.Exists(LockFile));
        Assert.False(File.Exists(LockFile));

        var next = DataDirectory.LockAsync(directory, time);

        Assert.True(next.IsCompletedSuccessfully);
        await using var taken = await next;
        Assert.NotNull(taken);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
