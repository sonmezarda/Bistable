using Bistable.Core.Projects;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-3 coverage. The default script must:
///   - read every source from the project,
///   - target the synthesis top (or fall back to the project top),
///   - include flatten / techmap stages when the config asks for them,
///   - write JSON + synthesised Verilog to configured paths.
///
/// We assert against the script text rather than running yosys so the tests
/// stay deterministic + don't require the binary.
/// </summary>
public sealed class YosysScriptBuilderTests
{
    private static ProjectConfiguration MinimalProject(string projectDirectory) => new()
    {
        TopModule = "top",
        Sources = ["foo.sv", "bar.sv"],
    };

    [Fact]
    public void Build_EmitsReadVerilogHierarchyWriteJsonAndWriteVerilog()
    {
        string dir = Path.GetTempPath();
        SynthesisConfiguration synth = new(Enabled: true);
        string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

        Assert.Contains("read_verilog -sv", script);
        Assert.Contains("foo.sv", script);
        Assert.Contains("bar.sv", script);
        Assert.Contains("hierarchy -check -top top", script);
        Assert.Contains("proc", script);
        Assert.Contains("memory", script);
        Assert.Contains("write_json", script);
        Assert.Contains("write_verilog -noattr", script);
    }

    [Fact]
    public void Build_GenericCellsAddsTechmap()
    {
        string dir = Path.GetTempPath();
        SynthesisConfiguration synth = new(GenericCells: true);
        string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

        Assert.Contains("techmap", script);
    }

    [Fact]
    public void Build_GenericCellsFalseSkipsTechmap()
    {
        string dir = Path.GetTempPath();
        SynthesisConfiguration synth = new(GenericCells: false);
        string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

        Assert.DoesNotContain("techmap", script);
    }

    [Fact]
    public void Build_FlattenAddsFlattenStage()
    {
        string dir = Path.GetTempPath();
        SynthesisConfiguration synth = new(Flatten: true);
        string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

        Assert.Contains("flatten", script);
    }

    [Fact]
    public void Build_SynthesisTopOverridesProjectTop()
    {
        string dir = Path.GetTempPath();
        SynthesisConfiguration synth = new(TopModule: "explicit_top");
        string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

        Assert.Contains("hierarchy -check -top explicit_top", script);
    }

    [Fact]
    public void Build_RelativeOutputJsonIsResolvedAgainstProjectDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bistable-yosys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            SynthesisConfiguration synth = new(OutputJson: ".bistable/synthesis/foo.json");
            string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

            string expected = Path.Combine(dir, ".bistable/synthesis/foo.json");
            Assert.Contains(expected, script);
            // Side-effect: build creates the output directory so yosys can write into it.
            Assert.True(Directory.Exists(Path.GetDirectoryName(expected)!));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Build_RelativeOutputVerilogIsResolvedAgainstProjectDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bistable-yosys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            SynthesisConfiguration synth = new(OutputVerilog: ".bistable/synthesis/foo.sv");
            string script = YosysScriptBuilder.Build(MinimalProject(dir), synth, dir);

            string expected = Path.Combine(dir, ".bistable/synthesis/foo.sv");
            Assert.Contains(expected, script);
            Assert.True(Directory.Exists(Path.GetDirectoryName(expected)!));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
