using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Memory-side counterpart of <see cref="LiveProbeServiceTests"/>. Covers the
/// memory cache, the descriptor-refresh path, and the snapshot equality used
/// by <see cref="LiveProbeService.MemoryUpdated"/> change detection.
/// </summary>
public sealed class LiveProbeServiceMemoryTests
{
    // ─── pure unit tests ───────────────────────────────────────────────────

    [Fact]
    public void GetCachedMemory_UnreadPath_ReturnsNull()
    {
        LiveProbeService service = new();
        Assert.Null(service.GetCachedMemory("memory_demo.mem"));
    }

    [Fact]
    public async Task ReadMemoryAsync_NoWorker_ReturnsNull_DoesNotThrow()
    {
        LiveProbeService service = new();
        MemorySnapshot? snap = await service.ReadMemoryAsync("memory_demo.mem", 0, 8, CancellationToken.None);
        Assert.Null(snap);
    }

    [Fact]
    public void GetDescriptor_BeforeRefresh_ReturnsNull()
    {
        LiveProbeService service = new();
        Assert.Null(service.GetDescriptor("memory_demo.mem"));
    }

    [Fact]
    public void MemorySnapshot_Equality_ComparesCellsByValue()
    {
        MemorySnapshot a = new("p", 0, 8, ["0x00", "0xA2"]);
        MemorySnapshot b = new("p", 0, 8, ["0x00", "0xA2"]);
        MemorySnapshot c = new("p", 0, 8, ["0x00", "0xA3"]);
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }

    // ─── integration tests (real worker required) ─────────────────────────

    [Fact]
    public async Task ReadMemoryAsync_RealWorker_PopulatesCache_AndRaisesMemoryUpdated()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);
        List<MemorySnapshotUpdatedEventArgs> updates = [];
        service.MemoryUpdated += (_, e) => updates.Add(e);

        MemorySnapshot? snap = await service.ReadMemoryAsync("memory_demo.mem", 0, 8, CancellationToken.None);

        Assert.NotNull(snap);
        Assert.Equal("memory_demo.mem", snap!.Path);
        Assert.Equal((ulong)0, snap.StartAddress);
        Assert.Equal(8, snap.Cells.Count);
        Assert.NotNull(service.GetCachedMemory("memory_demo.mem"));
        Assert.Single(updates);
    }

    [Fact]
    public async Task ReadMemoryAsync_SameSnapshotTwice_RaisesMemoryUpdatedOnlyOnce()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);
        int updateCount = 0;
        service.MemoryUpdated += (_, _) => updateCount++;

        await service.ReadMemoryAsync("memory_demo.mem", 0, 8, CancellationToken.None);
        await service.ReadMemoryAsync("memory_demo.mem", 0, 8, CancellationToken.None);
        await service.ReadMemoryAsync("memory_demo.mem", 0, 8, CancellationToken.None);

        Assert.Equal(1, updateCount);
    }

    [Fact]
    public async Task InvalidateAll_DropsMemorySnapshotCache()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);
        await service.ReadMemoryAsync("memory_demo.mem", 0, 4, CancellationToken.None);
        Assert.NotNull(service.GetCachedMemory("memory_demo.mem"));

        service.InvalidateAll();
        Assert.Null(service.GetCachedMemory("memory_demo.mem"));
    }

    [Fact]
    public async Task RefreshDescriptorsAsync_PopulatesIsMemoryAndDepth_FromWorker()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);

        await service.RefreshDescriptorsAsync(CancellationToken.None);

        ProbeDescriptor? mem = service.GetDescriptor("memory_demo.mem");
        Assert.NotNull(mem);
        Assert.True(mem!.IsMemory);
        Assert.Equal(16, mem.MemoryDepth);

        ProbeDescriptor? clk = service.GetDescriptor("memory_demo.clk");
        Assert.NotNull(clk);
        Assert.False(clk!.IsMemory);
    }

    [Fact]
    public async Task AttachWorker_Null_DropsMemoryCache_AndDescriptors()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient worker = new(workerPath);

        LiveProbeService service = new();
        service.AttachWorker(worker);
        await service.RefreshDescriptorsAsync(CancellationToken.None);
        await service.ReadMemoryAsync("memory_demo.mem", 0, 4, CancellationToken.None);

        service.AttachWorker(null);

        Assert.False(service.HasWorker);
        Assert.Null(service.GetCachedMemory("memory_demo.mem"));
        Assert.Null(service.GetDescriptor("memory_demo.mem"));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static async Task<string> BuildMemoryDemoWorkerAsync()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "memory_demo");
        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "memory_demo.bistable.json"),
            CancellationToken.None);

        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-livememory-{Guid.NewGuid():N}.xml");
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
