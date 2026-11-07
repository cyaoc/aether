using Aether.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Aether.Core.Tests;

/// <summary>ADR 0017: a region runs one step at a time, in the order the steps came.</summary>
public sealed class RegionTests
{
    private readonly RecordingLogger logger = new();
    private readonly Regions regions;
    private readonly List<int> state = [];

    public RegionTests() => regions = new(TimeProvider.System, logger);

    private Region<List<int>> NewRegion() => regions.Create(_ => state);

    [Fact]
    public async Task Steps_run_in_the_order_they_came()
    {
        var region = NewRegion();
        for (var i = 0; i < 100; i++)
        {
            var step = i;
            if (step % 2 == 0) region.Post(s => s.Add(step));
            else _ = region.Run(s => s.Add(step));
        }

        Assert.Equal(Enumerable.Range(0, 100), await region.Run(s => s.ToArray()));
    }

    [Fact]
    public async Task A_step_that_throws_fails_only_its_own_run()
    {
        var region = NewRegion();

        var failing = region.Run(int (_) => throw new InvalidOperationException("坏了"));
        region.Post(s => s.Add(1));

        Assert.Equal("坏了", (await Assert.ThrowsAsync<InvalidOperationException>(() => failing)).Message);
        var after = await region.Run(s => s.ToArray());
        Assert.Equal([1], after);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_posted_step_that_throws_is_logged_and_the_next_step_runs()
    {
        var region = NewRegion();

        region.Post(void (_) => throw new InvalidOperationException("坏了"));
        region.Post(s => s.Add(1));

        var after = await region.Run(s => s.ToArray());
        Assert.Equal([1], after);
        Assert.Equal("坏了", Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error).Exception?.Message);
    }

    [Fact]
    public async Task A_stopped_region_drops_posts_and_cancels_runs()
    {
        var region = NewRegion();

        region.Stop();
        region.Post(s => s.Add(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => region.Run(s => s.Count));
        await regions.IdleAsync();
        Assert.Empty(state);
    }

    [Fact]
    public async Task Idle_waits_for_a_step_that_a_step_posted_to_another_region()
    {
        var first = NewRegion();
        var second = regions.Create(_ => new List<int>());
        var ran = false;

        first.Post(_ => second.Post(_ => ran = true));
        await regions.IdleAsync();

        Assert.True(ran);
    }
}
