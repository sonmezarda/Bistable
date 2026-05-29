using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Phase 3 end-to-end: build a Verilator worker with the AST flowing in
/// (so the probe table is populated) and exercise listProbes / readSignal /
/// writeSignal / forceSignal / releaseSignal over the JSON protocol.
/// </summary>
public sealed class HotProbeTests
{
    [Fact]
    public async Task ListProbes_ReturnsTopLevelPortsAndLocals()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("counter");
        await using SimulationWorkerClient client = new(workerPath);

        IReadOnlyList<ProbeDescriptor> probes = await client.ListProbesAsync(CancellationToken.None);

        Assert.NotEmpty(probes);
        Assert.Contains(probes, p => p.Path == "counter.clk" && p.Width == 1);
        Assert.Contains(probes, p => p.Path == "counter.rst_n" && p.Width == 1);
        Assert.Contains(probes, p => p.Path == "counter.enable" && p.Width == 1);
        Assert.Contains(probes, p => p.Path == "counter.count" && p.Width == 8);
        Assert.Contains(probes, p => p.Path == "counter.terminal" && p.Width == 1);
    }

    [Fact]
    public async Task ReadSignal_ReflectsLiveModelState_AcrossTicks()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("counter");
        await using SimulationWorkerClient client = new(workerPath);

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

        SignalReadResult zero = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal(8, zero.Width);
        Assert.Equal((ulong)0, ParseHex(zero.Value));

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        SignalReadResult one = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)1, ParseHex(one.Value));

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        SignalReadResult two = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)2, ParseHex(two.Value));
    }

    [Fact]
    public async Task ReadSignal_UnknownPath_ReturnsError()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("counter");
        await using SimulationWorkerClient client = new(workerPath);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ReadSignalAsync("counter.does_not_exist", CancellationToken.None));
        Assert.Contains("unknown probe path", ex.Message);
    }

    [Fact]
    public async Task WriteSignal_OverwritesFlipFlopValue_VisibleOnReadBack()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("counter");
        await using SimulationWorkerClient client = new(workerPath);

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
        await client.WriteSignalAsync("counter.count", "0xA5", CancellationToken.None);

        SignalReadResult read = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)0xA5, ParseHex(read.Value));
    }

    [Fact]
    public async Task ForceSignal_SurvivesEvalAndTick_UntilReleased()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("counter");
        await using SimulationWorkerClient client = new(workerPath);

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Reset), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"), CancellationToken.None);

        // Force the counter to 0x42. The forced value must persist across the
        // next tick because apply_forced_signals runs at the top of every eval.
        await client.ForceSignalAsync("counter.count", "0x42", CancellationToken.None);

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        SignalReadResult stillForced = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)0x42, ParseHex(stillForced.Value));

        await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        SignalReadResult stillForcedTwo = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)0x42, ParseHex(stillForcedTwo.Value));

        // After release, the FF advances normally on the next tick (0x42 -> 0x43).
        await client.ReleaseSignalAsync("counter.count", CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: "clk"), CancellationToken.None);
        SignalReadResult advanced = await client.ReadSignalAsync("counter.count", CancellationToken.None);
        Assert.Equal((ulong)0x43, ParseHex(advanced.Value));
    }

    /// <summary>
    /// Smoke test on the production-grade sample: arnicomp is the 8-bit CPU
    /// that motivated Phase 3 — verifying probe table works for a hierarchical
    /// design with many sub-instances and registers.
    /// </summary>
    [Fact]
    public async Task Arnicomp_ProbeTablePopulatedForAllHierarchyLevels()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("arnicomp");
        await using SimulationWorkerClient client = new(workerPath);

        IReadOnlyList<ProbeDescriptor> probes = await client.ListProbesAsync(CancellationToken.None);

        Assert.NotEmpty(probes);
        // Top-level ports are addressable.
        Assert.Contains(probes, p => p.Path == "arnicomp_top.clk");
        Assert.Contains(probes, p => p.Path == "arnicomp_top.pc_out");
        // Nested-instance signals from sub-modules (accumulator, ALU, PC etc.).
        Assert.Contains(probes, p => p.Path.StartsWith("arnicomp_top.", StringComparison.Ordinal)
                                     && p.Path.Split('.').Length >= 3);

        // Read a top-level port through the probe table.
        SignalReadResult pc = await client.ReadSignalAsync("arnicomp_top.pc_out", CancellationToken.None);
        Assert.Equal(16, pc.Width);
    }

    [Fact]
    public async Task Hierarchy_NestedInstanceProbes_AreAddressable()
    {
        var (workerPath, _, _) = await BuildWorkerAsync("hierarchy");
        await using SimulationWorkerClient client = new(workerPath);

        IReadOnlyList<ProbeDescriptor> probes = await client.ListProbesAsync(CancellationToken.None);

        // Sanity: at least one nested-instance probe path appears.
        Assert.Contains(probes, p => p.Path.StartsWith("system_top.u_core.", StringComparison.Ordinal));

        // Drive a + b and read the nested combinational sum via probe.
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "0x10"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "0x05"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.Eval), CancellationToken.None);

        SignalReadResult sum = await client.ReadSignalAsync("system_top.u_core.u_logic.sum", CancellationToken.None);
        Assert.Equal((ulong)0x15, ParseHex(sum.Value));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static async Task<(string ExecutablePath, ModuleMetadata Metadata, DesignAst Ast)>
        BuildWorkerAsync(string sampleName)
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", sampleName);
        ProjectConfiguration configuration = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, $"{sampleName}.bistable.json"),
            CancellationToken.None);

        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-hotprobe-{sampleName}-{Guid.NewGuid():N}.xml");
        VerilatorTool tool = new();
        await tool.GenerateXmlAsync(configuration, sampleDirectory, xmlPath, CancellationToken.None);

        try
        {
            ModuleMetadata metadata = VerilatorXmlParser.Parse(xmlPath);
            DesignAst ast = new VerilatorXmlAstReader().Read(xmlPath);
            SimulationWorkerBuildResult build = await new SimulationWorkerBuilder().BuildAsync(
                configuration,
                metadata,
                sampleDirectory,
                CancellationToken.None,
                progress: null,
                designAst: ast);
            return (build.ExecutablePath, metadata, ast);
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
