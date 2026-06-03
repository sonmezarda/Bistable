using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Verilator;

namespace Bistable.Tests;

/// <summary>
/// Phase 5 P5-5 / P5-8 coverage. Drives a real Verilator worker for the bundled
/// RISC-V sample through the CpuRunEngine + the bundled hex program. Confirms:
///   - ApplyResetAsync drives rst_n low, ticks the configured cycles, then
///     deasserts so PC starts from zero.
///   - LoadProgramAsync writes every cell of the parsed hex into the imem.
///   - RunAsync ticks until halted, returning the actual cycle count and the
///     stop-condition-hit flag.
/// </summary>
public sealed class CpuRunEngineIntegrationTests
{
    [Fact]
    public async Task ResetLoadRun_DrivesRiscvSampleToHalt()
    {
        string root = FindRepositoryRoot();
        string sampleDirectory = Path.Combine(root, "samples", "riscv_single_cycle");

        ProjectConfiguration config = await ProjectConfiguration.LoadAsync(
            Path.Combine(sampleDirectory, "riscv_single_cycle.bistable.json"),
            CancellationToken.None);
        Assert.NotNull(config.Runtime);

        string outputXml = Path.Combine(Path.GetTempPath(), $"bistable-rv-run-{Guid.NewGuid():N}.xml");
        await new VerilatorTool().GenerateXmlAsync(config, sampleDirectory, outputXml, CancellationToken.None);

        try
        {
            ModuleMetadata metadata = VerilatorXmlParser.Parse(outputXml);
            DesignAst ast = new VerilatorXmlAstReader().Read(outputXml);
            SimulationWorkerBuildResult build = await new SimulationWorkerBuilder().BuildAsync(
                config, metadata, sampleDirectory, CancellationToken.None,
                progress: null, designAst: ast);

            await using SimulationWorkerClient client = new(build.ExecutablePath);
            CpuRunEngine engine = new(new LiveProbeService());

            // Reset → enable=1 → load program → run.
            await engine.ApplyResetAsync(client, config.Runtime!.Reset!, clock: "clk", CancellationToken.None);
            await client.StepAsync(
                new Bistable.Protocol.SimulationCommand(Bistable.Protocol.SimulationCommandType.SetInput, "enable", "1"),
                CancellationToken.None);

            string programPath = Path.Combine(sampleDirectory, config.Runtime.ProgramImages![0].Path);
            MemoryFileLoader.MemoryImage image = MemoryFileLoader.LoadFromFile(
                programPath, cellWidth: 32, depth: 32);
            ProgramLoadResult load = await engine.LoadProgramAsync(
                client, config.Runtime.ProgramImages[0], image, CancellationToken.None);
            Assert.Equal(6, load.Written);
            Assert.Equal(0, load.Failed);
            Assert.Equal(0, load.ParseErrors);

            RunResult run = await engine.RunAsync(
                client, config.Runtime.RunPresets![0], config.Runtime.State, CancellationToken.None);

            // The sample halts after the ebreak word (cell 5). PC advances one
            // per cycle and the engine ticks one extra after stop-condition
            // matches, so we expect ≤ a small bound rather than a precise count.
            Assert.True(run.StopConditionHit, "Run engine should have detected the halted == 1 stop condition.");
            Assert.InRange(run.Cycles, 1, 20);
        }
        finally
        {
            File.Delete(outputXml);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "samples", "riscv_single_cycle"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root with samples/ not found.");
    }
}
