using System.Diagnostics;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-3 + P6-4 end-to-end: write a tiny SystemVerilog file, run the
/// real yosys binary against the generated script, then parse the resulting
/// JSON. Skipped when yosys isn't installed so CI without the binary stays
/// green; the fixture-driven <see cref="YosysJsonReaderTests"/> still cover
/// parser correctness.
/// </summary>
[Trait("Category", "Integration")]
public sealed class YosysRoundTripIntegrationTests
{
    private static bool YosysAvailable()
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo
            {
                FileName = "yosys",
                Arguments = "-V",
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

    [Fact]
    public async Task BuildScript_Run_Parse_TinyAndGate()
    {
        if (!YosysAvailable()) return; // skip — see test docstring.

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-yosys-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            string svPath = Path.Combine(workDir, "tiny_and.sv");
            await File.WriteAllTextAsync(svPath,
                "module tiny_and(input wire a, input wire b, output wire y);\n" +
                "    assign y = a & b;\n" +
                "endmodule\n");

            ProjectConfiguration project = new()
            {
                TopModule = "tiny_and",
                Sources = ["tiny_and.sv"],
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "tiny_and.json",
                    OutputVerilog: "tiny_and_synth.sv"),
            };

            string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
            string scriptPath = Path.Combine(workDir, "synth.ys");
            await File.WriteAllTextAsync(scriptPath, script);

            YosysTool tool = new();
            await tool.RunScriptAsync(scriptPath, workDir, CancellationToken.None);

            string outputJson = Path.Combine(workDir, "tiny_and.json");
            Assert.True(File.Exists(outputJson), "Yosys should have produced the configured output JSON.");
            string outputVerilog = Path.Combine(workDir, "tiny_and_synth.sv");
            Assert.True(File.Exists(outputVerilog), "Yosys should have produced the configured synthesized Verilog.");

            GateNetlist netlist = await YosysJsonReader.ReadFileAsync(outputJson, CancellationToken.None);
            Assert.Equal("tiny_and", netlist.TopModule);
            GateModule module = netlist.Modules["tiny_and"];
            GateCell cell = Assert.Single(module.Cells);
            Assert.Equal("$_AND_", cell.Type);
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task BuildScript_FlattenEnabled_PreservesViewerJsonHierarchy()
    {
        if (!YosysAvailable()) return;

        string workDir = Path.Combine(Path.GetTempPath(), $"bistable-yosys-hierarchy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workDir, "hierarchy.sv"), """
                module child(input logic a, input logic b, output logic y);
                    assign y = a & b;
                endmodule

                module top(input logic a, input logic b, output logic y);
                    child u_child(.a(a), .b(b), .y(y));
                endmodule
                """);

            ProjectConfiguration project = new()
            {
                TopModule = "top",
                Sources = ["hierarchy.sv"],
                Synthesis = new SynthesisConfiguration(
                    Enabled: true,
                    OutputJson: "hierarchy.json",
                    OutputVerilog: "hierarchy_synth.sv",
                    Flatten: true),
            };

            string script = YosysScriptBuilder.Build(project, project.Synthesis!, workDir);
            string scriptPath = Path.Combine(workDir, "synth.ys");
            await File.WriteAllTextAsync(scriptPath, script);
            await new YosysTool().RunScriptAsync(scriptPath, workDir, CancellationToken.None);

            GateNetlist netlist = await YosysJsonReader.ReadFileAsync(
                Path.Combine(workDir, "hierarchy.json"),
                CancellationToken.None);

            Assert.Contains("child", netlist.Modules.Keys);
            GateCell instance = Assert.Single(netlist.Modules["top"].Cells);
            Assert.Equal("child", instance.Type);
            Assert.True(File.Exists(Path.Combine(workDir, "hierarchy_synth.sv")));
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
