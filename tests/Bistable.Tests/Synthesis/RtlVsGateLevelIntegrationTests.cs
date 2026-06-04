using System.Diagnostics;
using Bistable.App.Services;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6.5 P6.5-11: full-stack RTL ↔ gate-level smoke compare.
///
/// Build path: SV → (RTL pipeline) → RTL worker + (Yosys pipeline) → gate
/// worker. Both workers are driven with the same Reset / SetInput / Tick
/// sequence; the comparator asserts every top-level port matches every cycle.
/// If Yosys ever emits a netlist that doesn't behave like the source RTL,
/// this test catches the first divergent cycle and reports which signal
/// disagreed.
///
/// Skipped when either yosys or verilator isn't installed so CI without the
/// toolchain stays green. Comparator semantics are unit-tested in
/// <see cref="RtlVsGateLevelComparatorTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RtlVsGateLevelIntegrationTests
{
    [Fact]
    public async Task ToggleCounter_RtlAndGateLevel_AgreeForEveryCycle()
    {
        if (!YosysAvailable() || !VerilatorAvailable()) return;

        // Small enough that yosys + verilator finish in a few seconds, but
        // exercises the load-bearing axes: clocked FF, async reset, an
        // enable gate, and a multi-bit output port. If any of these diverge
        // between RTL and gate-level the diff will pinpoint which.
        const string source = """
            module toggle_counter(
                input  wire        clk,
                input  wire        rst_n,
                input  wire        enable,
                output reg  [3:0]  count,
                output wire        msb
            );
                always @(posedge clk or negedge rst_n) begin
                    if (!rst_n)       count <= 4'h0;
                    else if (enable)  count <= count + 4'h1;
                end
                assign msb = count[3];
            endmodule
            """;

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-rtl-vs-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            string svPath = Path.Combine(workDir, "toggle_counter.sv");
            await File.WriteAllTextAsync(svPath, source);

            ProjectConfiguration rtlProject = new()
            {
                TopModule = "toggle_counter",
                Sources = ["toggle_counter.sv"],
                // Validator only accepts "vcd"; tracing is irrelevant to this test.
                Trace = new TraceConfiguration(Enabled: false),
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "synth/toggle_counter.json",
                    OutputVerilog: "synth/toggle_counter.sv"),
            };

            // ── RTL worker ────────────────────────────────────────────────
            SimulationWorkerBuildResult rtlBuild = await BuildRtlWorkerAsync(rtlProject, workDir);
            await using SimulationWorkerClient rtlClient = new(rtlBuild.ExecutablePath);

            // ── Gate-level worker (via Yosys → Verilator) ─────────────────
            await RunYosysAsync(rtlProject, workDir);
            GateLevelWorkerBuildService gateBuilder = new();
            GateLevelWorkerBuildResult gate = await gateBuilder.BuildAsync(
                rtlProject, rtlProject.Synthesis!, workDir, CancellationToken.None);
            await using SimulationWorkerClient gateClient = new(gate.Worker.ExecutablePath);

            // ── Run both workers in lockstep ──────────────────────────────
            RtlVsGateLevelComparator comparator = new();
            CompareProgram program = new()
            {
                Clock = "clk",
                Cycles = 20,
                Setup =
                [
                    new SimulationCommand(SimulationCommandType.SetInput, "rst_n", "0"),
                    new SimulationCommand(SimulationCommandType.SetInput, "enable", "1"),
                ],
                SignalsToCompare = ["count", "msb"],
            };
            CompareReport report = await comparator.CompareProgramAsync(rtlClient, gateClient, program);

            Assert.True(report.AllMatch,
                "RTL and gate-level outputs diverged:\n" + report.FormatSummary());
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ── Toolchain build helpers ───────────────────────────────────────────

    private static async Task<SimulationWorkerBuildResult> BuildRtlWorkerAsync(
        ProjectConfiguration project, string workDir)
    {
        string xmlPath = Path.Combine(workDir, $"rtl-{Guid.NewGuid():N}.xml");
        await new VerilatorTool().GenerateXmlAsync(project, workDir, xmlPath, CancellationToken.None);
        try
        {
            ModuleMetadata metadata = VerilatorXmlParser.Parse(xmlPath);
            DesignAst ast = new VerilatorXmlAstReader().Read(xmlPath);
            return await new SimulationWorkerBuilder().BuildAsync(
                project, metadata, workDir, CancellationToken.None, progress: null, designAst: ast);
        }
        finally
        {
            if (File.Exists(xmlPath)) File.Delete(xmlPath);
        }
    }

    private static async Task RunYosysAsync(ProjectConfiguration project, string workDir)
    {
        string scriptPath = Path.Combine(workDir, "synth.ys");
        string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
        await File.WriteAllTextAsync(scriptPath, script);
        await new YosysTool().RunScriptAsync(scriptPath, workDir, CancellationToken.None);
    }

    private static bool YosysAvailable() => ToolAvailable("yosys", "-V");
    private static bool VerilatorAvailable() => ToolAvailable("verilator", "--version");

    private static bool ToolAvailable(string fileName, string args)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
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
