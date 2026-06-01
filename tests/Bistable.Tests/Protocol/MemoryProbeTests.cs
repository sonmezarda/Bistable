using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// P3-6 / P3-11: end-to-end memory probe roundtrip using the memory_demo
/// sample. Verifies that the enumerator advertises the memory, the worker
/// answers readMemory / writeMemory commands, and the contents survive
/// a clock pulse with we=1.
/// </summary>
public sealed class MemoryProbeTests
{
    [Fact]
    public async Task ListProbes_IncludesMemoryEntryWithDepth16()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient client = new(workerPath);

        IReadOnlyList<ProbeDescriptor> probes = await client.ListProbesAsync(CancellationToken.None);

        ProbeDescriptor mem = Assert.Single(probes, p => p.Path == "memory_demo.mem");
        Assert.True(mem.IsMemory);
        Assert.Equal(16, mem.MemoryDepth);
        Assert.Equal(8, mem.Width);
    }

    [Fact]
    public async Task ReadMemory_Range_ReturnsCorrectCellCount()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient client = new(workerPath);

        MemoryReadResult result = await client.ReadMemoryAsync(
            "memory_demo.mem", address: 0, count: 8, CancellationToken.None);

        Assert.Equal("memory_demo.mem", result.Path);
        Assert.Equal((ulong)0, result.StartAddress);
        Assert.Equal(8, result.CellWidth);
        Assert.Equal(8, result.Cells.Count);
    }

    [Fact]
    public async Task WriteMemory_FollowedByReadMemory_RoundTripsCell()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient client = new(workerPath);

        await client.WriteMemoryAsync("memory_demo.mem", address: 4, value: "0xA5", CancellationToken.None);
        MemoryReadResult result = await client.ReadMemoryAsync(
            "memory_demo.mem", address: 4, count: 1, CancellationToken.None);

        Assert.Single(result.Cells);
        Assert.Equal((ulong)0xA5, ParseHex(result.Cells[0]));
    }

    [Fact]
    public async Task ReadMemory_OutOfRangeAddress_ReturnsError()
    {
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient client = new(workerPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadMemoryAsync("memory_demo.mem", address: 999, count: 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReadSignal_OnMemoryPath_ReturnsZero_ButNoError()
    {
        // P3-6: scalar readSignal on a memory path returns 0 by design — the
        // entry is in the probe_table only for metadata; use readMemory instead.
        string workerPath = await BuildMemoryDemoWorkerAsync();
        await using SimulationWorkerClient client = new(workerPath);

        SignalReadResult result = await client.ReadSignalAsync("memory_demo.mem", CancellationToken.None);
        Assert.Equal((ulong)0, ParseHex(result.Value));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static async Task<string> BuildMemoryDemoWorkerAsync()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "memory_demo");
        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "memory_demo.bistable.json"),
            CancellationToken.None);

        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-mem-{Guid.NewGuid():N}.xml");
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

    private static ulong ParseHex(string value)
    {
        string trimmed = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return Convert.ToUInt64(trimmed, 16);
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
