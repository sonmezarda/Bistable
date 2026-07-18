using Bistable.Engine;

namespace Bistable.Tests.Engine;

/// <summary>
/// Phase 9.5 live-loop acceptance-gate tests for the engine-side simulation
/// session. Categories 1 (drive→frame), 3 (single batched read), 4 (no process
/// leak). Real Verilator worker over the `counter` sample.
/// </summary>
public sealed class SimulationSessionServiceTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DrivingInputEvaluatesToExpectedFrame()
    {
        EngineDesignLoadResult design = await LoadCounterAsync();
        await using SimulationSessionService session = new();

        SimulationSessionSnapshot snapshot = await session.StartAsync(design, CancellationToken.None);
        Assert.Equal("counter", snapshot.TopModule);
        Assert.Contains(snapshot.Ports, p => p.Name == "enable" && p.Direction == "Input");
        Assert.Contains(snapshot.Probes, p => p.Path == "counter.count" && p.Width == 8);

        // Deassert the active-low reset, drive enable=1, take a clock edge, and
        // confirm the counter advanced.
        await session.SetInputAsync("rst_n", "1", CancellationToken.None);
        await session.SetInputAsync("enable", "1", CancellationToken.None);
        SimulationFrameResult afterTick = await session.TickAsync("clk", CancellationToken.None);

        SimulationSignalValue count = Assert.Single(afterTick.Signals, s => s.Signal == "count");
        Assert.Equal("1", count.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FrameRefreshUsesOneBatchedReadNotOnePerSignal()
    {
        EngineDesignLoadResult design = await LoadCounterAsync();
        await using SimulationSessionService session = new();
        await session.StartAsync(design, CancellationToken.None);

        string[] paths = ["counter.count", "counter.enable", "counter.terminal", "counter.clk"];
        long before = session.CompletedRoundTrips;
        SimulationReadResult result = await session.ReadSignalsAsync(paths, CancellationToken.None);

        // One stdin/stdout round-trip for the whole visible set, not four.
        Assert.Equal(1, session.CompletedRoundTrips - before);
        Assert.Equal(4, result.Results.Count);
        Assert.All(result.Results, outcome => Assert.Null(outcome.Error));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidationRejectsBadValueWithoutTouchingWorker()
    {
        EngineDesignLoadResult design = await LoadCounterAsync();
        await using SimulationSessionService session = new();
        await session.StartAsync(design, CancellationToken.None);

        long before = session.CompletedRoundTrips;
        // enable is 1-bit; 2 overflows the width.
        await Assert.ThrowsAsync<SimulationValidationException>(
            () => session.SetInputAsync("enable", "2", CancellationToken.None));
        await Assert.ThrowsAsync<SimulationValidationException>(
            () => session.SetInputAsync("enable", "0xZZ", CancellationToken.None));

        // No worker command was issued for either rejected value.
        Assert.Equal(before, session.CompletedRoundTrips);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DisposeLeavesNoLiveWorkerProcess()
    {
        EngineDesignLoadResult design = await LoadCounterAsync();
        SimulationSessionService session = new();
        await session.StartAsync(design, CancellationToken.None);
        Assert.True(session.HasWorker);

        await session.DisposeAsync();

        Assert.False(session.HasWorker);
        // Further stepping fails cleanly rather than leaking a second worker.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.StartAsync(design, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReloadAdvancesGenerationAndSwapsWorker()
    {
        EngineDesignLoadResult design = await LoadCounterAsync();
        await using SimulationSessionService session = new();

        await session.StartAsync(design, CancellationToken.None);
        long firstGeneration = session.Generation;

        // A reload builds a fresh worker, disposes the old one, and advances the
        // generation so any late result from the first worker would be dropped.
        await session.StartAsync(design, CancellationToken.None);

        Assert.True(session.Generation > firstGeneration);
        Assert.True(session.HasWorker);
    }

    private static async Task<EngineDesignLoadResult> LoadCounterAsync()
    {
        string root = FindRepositoryRoot();
        string projectFile = Path.Combine(root, "samples", "counter", "counter.bistable.json");
        return await new DesignElaborationService().LoadAsync(projectFile, CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bistable.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
