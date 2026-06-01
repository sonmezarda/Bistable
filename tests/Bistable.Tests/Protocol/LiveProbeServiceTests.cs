using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// <see cref="LiveProbeService"/> is the cache/event bridge between the GUI's
/// schematic selection and the worker's Phase 3 probe API. These tests pin
/// the cache invalidation semantics (pure unit tests) plus one end-to-end
/// roundtrip against a real worker.
/// </summary>
public sealed class LiveProbeServiceTests
{
    // ─── pure unit tests (no worker required) ─────────────────────────────

    [Fact]
    public void HasWorker_IsFalse_WhenNoWorkerAttached()
    {
        LiveProbeService service = new();
        Assert.False(service.HasWorker);
    }

    [Fact]
    public void GetCached_UnreadPath_ReturnsNull()
    {
        LiveProbeService service = new();
        Assert.Null(service.GetCached("counter.count"));
    }

    [Fact]
    public async Task ReadAsync_NoWorker_ReturnsNull_DoesNotThrow()
    {
        LiveProbeService service = new();
        string? value = await service.ReadAsync("counter.count", CancellationToken.None);
        Assert.Null(value);
    }

    [Fact]
    public void AttachWorker_Null_ClearsCache()
    {
        LiveProbeService service = new();
        // Seed cache via internal API path — exercise the public surface only:
        // attach a null worker first (no-op), then we can't reach the cache.
        // Instead we cover this via integration test below where the cache
        // is populated, then detached, then verified empty.
        service.AttachWorker(null);
        Assert.False(service.HasWorker);
        Assert.Null(service.GetCached("any.path"));
    }

    // ─── integration: real worker required ────────────────────────────────

    [Fact]
    public async Task ReadAsync_RealWorker_PopulatesCache_AndRaisesValueUpdated()
    {
        string workerPath = await BuildCounterWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

        LiveProbeService service = new();
        service.AttachWorker(worker);

        List<ProbeValueUpdatedEventArgs> updates = [];
        service.ValueUpdated += (_, e) => updates.Add(e);

        string? value = await service.ReadAsync("counter.count", CancellationToken.None);

        Assert.NotNull(value);
        Assert.Equal("0x0", value);
        Assert.Equal("0x0", service.GetCached("counter.count"));
        Assert.Single(updates);
        Assert.Equal("counter.count", updates[0].Path);
    }

    [Fact]
    public async Task ReadAsync_SameValueTwice_RaisesValueUpdatedOnlyOnce()
    {
        string workerPath = await BuildCounterWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);

        LiveProbeService service = new();
        service.AttachWorker(worker);

        int updateCount = 0;
        service.ValueUpdated += (_, _) => updateCount++;

        await service.ReadAsync("counter.count", CancellationToken.None);
        await service.ReadAsync("counter.count", CancellationToken.None);
        await service.ReadAsync("counter.count", CancellationToken.None);

        Assert.Equal(1, updateCount);
    }

    [Fact]
    public async Task InvalidateAll_ForcesReReadOnNextGet()
    {
        string workerPath = await BuildCounterWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

        LiveProbeService service = new();
        service.AttachWorker(worker);

        await service.ReadAsync("counter.count", CancellationToken.None);
        Assert.Equal("0x0", service.GetCached("counter.count"));

        // Tick (count → 1) WITHOUT invalidating; cache still says 0.
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        Assert.Equal("0x0", service.GetCached("counter.count"));

        // Invalidate + re-read sees the new value.
        service.InvalidateAll();
        Assert.Null(service.GetCached("counter.count"));
        string? fresh = await service.ReadAsync("counter.count", CancellationToken.None);
        Assert.Equal("0x1", fresh);
    }

    [Fact]
    public async Task ReadAsync_UnknownPath_SwallowsError_ReturnsNull()
    {
        string workerPath = await BuildCounterWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);

        string? value = await service.ReadAsync("counter.nonexistent", CancellationToken.None);
        Assert.Null(value);
        Assert.Null(service.GetCached("counter.nonexistent"));
    }

    [Fact]
    public async Task AttachWorker_Null_DropsCachedValues()
    {
        string workerPath = await BuildCounterWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);
        await worker.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);

        LiveProbeService service = new();
        service.AttachWorker(worker);
        await service.ReadAsync("counter.count", CancellationToken.None);
        Assert.NotNull(service.GetCached("counter.count"));

        service.AttachWorker(null);
        Assert.False(service.HasWorker);
        Assert.Null(service.GetCached("counter.count"));
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static async Task<string> BuildCounterWorkerAsync()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "counter");
        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "counter.bistable.json"),
            CancellationToken.None);

        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-liveprobe-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, xmlPath, CancellationToken.None);

        try
        {
            ModuleMetadata metadata = VerilatorXmlParser.Parse(xmlPath);
            DesignAst ast = new VerilatorXmlAstReader().Read(xmlPath);
            SimulationWorkerBuildResult build = await new SimulationWorkerBuilder().BuildAsync(
                configuration, metadata, sampleDirectory, CancellationToken.None,
                progress: null, designAst: ast);
            return build.ExecutablePath;
        }
        finally
        {
            File.Delete(xmlPath);
        }
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
