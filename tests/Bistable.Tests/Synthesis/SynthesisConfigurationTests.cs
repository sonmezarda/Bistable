using System.Text.Json;
using Bistable.Core.Projects;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-1 coverage. The Synthesis section must:
///   - round-trip cleanly through ProjectConfiguration.JsonOptions,
///   - default to "absent" when not in the JSON (no opt-in regression),
///   - keep its sensible defaults when only a subset of fields is supplied.
/// </summary>
public sealed class SynthesisConfigurationTests
{
    [Fact]
    public void Synthesis_RoundTripsThroughProjectConfigurationJson()
    {
        ProjectConfiguration original = new()
        {
            TopModule = "top",
            Sources = ["top.sv"],
            Synthesis = new SynthesisConfiguration(
                Enabled: true,
                Backend: "yosys",
                Script: "synth.ys",
                TopModule: "top",
                OutputJson: ".bistable/synthesis/top.json",
                GenericCells: true,
                Flatten: false),
        };

        string json = JsonSerializer.Serialize(original, ProjectConfiguration.JsonOptions);
        ProjectConfiguration? round = JsonSerializer.Deserialize<ProjectConfiguration>(json, ProjectConfiguration.JsonOptions);

        Assert.NotNull(round);
        Assert.NotNull(round!.Synthesis);
        Assert.True(round.Synthesis!.Enabled);
        Assert.Equal("yosys", round.Synthesis.Backend);
        Assert.Equal("synth.ys", round.Synthesis.Script);
        Assert.Equal("top", round.Synthesis.TopModule);
        Assert.Equal(".bistable/synthesis/top.json", round.Synthesis.OutputJson);
        Assert.True(round.Synthesis.GenericCells);
        Assert.False(round.Synthesis.Flatten);
    }

    [Fact]
    public void Synthesis_IsOptional_WhenAbsentFromJson()
    {
        string json = """
            {
              "topModule": "top",
              "sources": ["top.sv"]
            }
            """;
        ProjectConfiguration? config = JsonSerializer.Deserialize<ProjectConfiguration>(json, ProjectConfiguration.JsonOptions);
        Assert.NotNull(config);
        Assert.Null(config!.Synthesis);
    }

    [Fact]
    public void Synthesis_PartialFieldsKeepDefaults()
    {
        // Only Enabled supplied — every other field should fall to the record's
        // declared default so users don't have to re-state Backend="yosys" etc.
        string json = """
            {
              "topModule": "top",
              "sources": ["top.sv"],
              "synthesis": { "enabled": true }
            }
            """;
        ProjectConfiguration? config = JsonSerializer.Deserialize<ProjectConfiguration>(json, ProjectConfiguration.JsonOptions);

        Assert.NotNull(config?.Synthesis);
        Assert.True(config!.Synthesis!.Enabled);
        Assert.Equal("yosys", config.Synthesis.Backend);
        Assert.True(config.Synthesis.GenericCells);
        Assert.False(config.Synthesis.Flatten);
        Assert.Equal(".bistable/synthesis/netlist.json", config.Synthesis.OutputJson);
    }
}
