using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class ProjectReloadCoordinatorTests
{
    [Fact]
    public async Task Queue_NewSaveDuringReload_CancelsAndRunsLatestSet()
    {
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<IReadOnlyCollection<string>> completed = [];
        int calls = 0;
        using ProjectReloadCoordinator coordinator = new(async (paths, cancellationToken) =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            completed.Add(paths);
        });

        coordinator.Queue(["first.sv"]);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        coordinator.Queue(["second.sv", "third.svh"]);
        await coordinator.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, calls);
        IReadOnlyCollection<string> latest = Assert.Single(completed);
        Assert.Equal(2, latest.Count);
        Assert.Contains("second.sv", latest);
        Assert.Contains("third.svh", latest);
    }
}
