using Bistable.App.Services;
using Bistable.Core.Projects;

namespace Bistable.Tests.Synthesis;

public sealed class GateLevelWorkerBuildServiceTests
{
    [Fact]
    public void BuildGateLevelProject_UsesSynthesizedVerilogAndSeparateWorkerDirectory()
    {
        ProjectConfiguration rtl = new()
        {
            TopModule = "cpu_top",
            Sources = ["rtl/cpu_top.sv"],
            IncludeDirs = ["rtl/include"],
            Defines = new Dictionary<string, string> { ["SIM"] = "1" },
            Parameters = new Dictionary<string, string> { ["WIDTH"] = "32" },
            VerilatorOptions = ["--timing"],
            EnableInternalProbes = false,
            Trace = new TraceConfiguration(Enabled: true, Format: "vcd", Depth: 4),
        };
        SynthesisConfiguration synthesis = new(TopModule: "cpu_synth_top");

        ProjectConfiguration gate = GateLevelWorkerBuildService.BuildGateLevelProject(
            rtl,
            synthesis,
            "/tmp/bistable/synth/cpu_synth_top.sv");

        Assert.Equal("cpu_synth_top", gate.TopModule);
        Assert.Equal("cpu_synth_top__gate", gate.WorkerBuildName);
        Assert.Equal(["/tmp/bistable/synth/cpu_synth_top.sv"], gate.Sources);
        Assert.Empty(gate.IncludeDirs);
        Assert.Empty(gate.Defines);
        Assert.Empty(gate.Parameters);
        Assert.False(gate.EnableInternalProbes);
        Assert.Equal(rtl.Trace, gate.Trace);
        Assert.Null(gate.Synthesis);
        Assert.Contains("--timing", gate.VerilatorOptions);
        Assert.Contains("--Wno-UNOPTFLAT", gate.VerilatorOptions);
    }

    [Fact]
    public async Task BuildAsync_ThrowsBeforeVerilatorWhenSynthesizedVerilogMissing()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bistable-gate-worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            ProjectConfiguration rtl = new()
            {
                TopModule = "top",
                Sources = ["top.sv"],
            };
            SynthesisConfiguration synthesis = new(OutputVerilog: ".bistable/synthesis/missing.sv");
            GateLevelWorkerBuildService service = new();

            FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.BuildAsync(rtl, synthesis, dir));

            Assert.Contains("missing.sv", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
