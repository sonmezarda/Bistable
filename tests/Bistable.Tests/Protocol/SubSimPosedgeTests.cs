using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Tests.Protocol;

/// <summary>
/// Reproduces the reg_cell isolated-simulation flow the user reported:
/// drive rst_n / we / oe high, set d, toggle clk, expect out to latch.
/// Locks in the "PushInputs runs SetInput per signal, posedge fires" semantics
/// that the GUI's live-eval pipeline depends on.
/// </summary>
public sealed class SubSimPosedgeTests
{
    [Fact]
    public async Task TogglingClock_AfterInputsSet_LatchesData()
    {
        string workerPath = await BuildSubSimWorkerAsync("arnicomp", "reg_cell", parameter: "W", value: "8");
        await using SimulationWorkerClient client = new(workerPath);

        // 1) Drive control signals high. SetInput evaluates after each push.
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "rst_n", "1"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "we",    "1"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "oe",    "1"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "d",     "0x30"), CancellationToken.None);

        // 2) Toggle clk 0 → 1. The rising edge inside SetInput should latch d
        //    into reg_q. The combinational `out = oe ? reg_q : 0` then exposes
        //    the latched value on the boundary.
        SimulationFrame frame = await client.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, "clk", "1"),
            CancellationToken.None);

        SignalSample @out = Assert.Single(frame.Signals, s => s.Signal == "out");
        Assert.Equal("48", @out.Value);   // 0x30 in decimal

        // 3) Toggle clk back to 0 — no edge effect on FF, out stays.
        SimulationFrame frame2 = await client.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, "clk", "0"),
            CancellationToken.None);
        SignalSample out2 = Assert.Single(frame2.Signals, s => s.Signal == "out");
        Assert.Equal("48", out2.Value);
    }

    [Fact]
    public async Task TogglingClock_WhenWeIsLow_DoesNotLatch()
    {
        string workerPath = await BuildSubSimWorkerAsync("arnicomp", "reg_cell", parameter: "W", value: "8");
        await using SimulationWorkerClient client = new(workerPath);

        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "rst_n", "1"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "we",    "0"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "oe",    "1"), CancellationToken.None);
        await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "d",     "0x77"), CancellationToken.None);

        SimulationFrame frame = await client.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, "clk", "1"),
            CancellationToken.None);

        SignalSample @out = Assert.Single(frame.Signals, s => s.Signal == "out");
        Assert.Equal("0", @out.Value);   // we=0 → FF holds reset value
    }

    private static async Task<string> BuildSubSimWorkerAsync(string sampleName, string subModule, string parameter, string value)
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", sampleName);
        ProjectConfiguration baseConfig = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, $"{sampleName}.bistable.json"),
            CancellationToken.None);
        ProjectConfiguration configuration = baseConfig with
        {
            TopModule = subModule,
            Parameters = new Dictionary<string, string>(baseConfig.Parameters)
            {
                [parameter] = value
            }
        };

        string xmlPath = Path.Combine(Path.GetTempPath(), $"bistable-subsim-{Guid.NewGuid():N}.xml");
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
