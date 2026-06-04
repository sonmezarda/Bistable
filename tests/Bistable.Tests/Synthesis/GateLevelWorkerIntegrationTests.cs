using System.Diagnostics;
using Bistable.App.Services;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

[Trait("Category", "Integration")]
public sealed class GateLevelWorkerIntegrationTests
{
    [Fact]
    public async Task YosysVerilogOutput_BuildsAndEvaluatesGateLevelWorker()
    {
        if (!ToolAvailable("yosys", "-V") || !ToolAvailable("verilator", "--version"))
        {
            return;
        }

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-gate-worker-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workDir, "tiny_and.sv"), """
                module tiny_and(input logic a, input logic b, output logic y);
                    assign y = a & b;
                endmodule
                """);

            ProjectConfiguration project = new()
            {
                TopModule = "tiny_and",
                Sources = ["tiny_and.sv"],
                EnableInternalProbes = false,
                Trace = new TraceConfiguration(Enabled: true, Format: "vcd", Depth: 1),
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "tiny_and.json",
                    OutputVerilog: "tiny_and_synth.sv"),
            };

            string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
            string scriptPath = Path.Combine(workDir, "synth.ys");
            await File.WriteAllTextAsync(scriptPath, script);
            await new YosysTool().RunScriptAsync(scriptPath, workDir, CancellationToken.None);

            GateLevelWorkerBuildResult build = await new GateLevelWorkerBuildService().BuildAsync(
                project,
                project.Synthesis!,
                workDir,
                CancellationToken.None);

            Assert.Equal("tiny_and__gate", build.Project.WorkerBuildName);
            Assert.True(File.Exists(build.Worker.ExecutablePath));

            await using SimulationWorkerClient client = new(build.Worker.ExecutablePath);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "a", "1"), CancellationToken.None);
            await client.StepAsync(new SimulationCommand(SimulationCommandType.SetInput, "b", "1"), CancellationToken.None);
            SimulationFrame frame = await client.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                CancellationToken.None);

            SignalSample y = Assert.Single(frame.Signals, static sample => sample.Signal == "y");
            Assert.Equal("1", y.Value);
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool ToolAvailable(string executable, string arguments)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
