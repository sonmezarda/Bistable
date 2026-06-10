using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;

namespace Bistable.Tests;

/// <summary>
/// Phase 6.5 Wave 5 W5-1: pin the routing-quality presets and the structured
/// → ELK option dictionary mapping. These knobs are the only knobs that move
/// layout time on real designs; if a preset silently drifts the regression
/// shows up as either a render slowdown (Balanced → Production drift) or a
/// quality cliff (Balanced → FastPreview drift). Tests live at the data layer
/// so they don't need a running Node/ELK process.
/// </summary>
public sealed class SchematicLayoutOptionsTests
{
    // ── ToElkOptions mapping ──────────────────────────────────────────────

    [Fact]
    public void ToElkOptions_EmitsLayeredRightHierarchicalSchematicSkeleton()
    {
        // Algorithm + direction + hierarchy handling are fixed across all
        // presets — they describe "a schematic", not "how nice it should look".
        SchematicLayoutOptions options = new(
            EdgeRouting: "ORTHOGONAL", LayeredThoroughness: 3,
            NodeNodeSpacing: 40, NodeNodeSpacingBetweenLayers: 80);

        Dictionary<string, string> dict = options.ToElkOptions();

        Assert.Equal("layered", dict["elk.algorithm"]);
        Assert.Equal("RIGHT", dict["elk.direction"]);
        Assert.Equal("INCLUDE_CHILDREN", dict["elk.hierarchyHandling"]);
    }

    [Fact]
    public void ToElkOptions_MapsTunableFieldsToTheirElkOptionKeys()
    {
        SchematicLayoutOptions options = new(
            EdgeRouting: "POLYLINE", LayeredThoroughness: 5,
            NodeNodeSpacing: 25, NodeNodeSpacingBetweenLayers: 60);

        Dictionary<string, string> dict = options.ToElkOptions();

        Assert.Equal("POLYLINE", dict["elk.edgeRouting"]);
        Assert.Equal("5", dict["elk.layered.thoroughness"]);
        Assert.Equal("25", dict["elk.spacing.nodeNode"]);
        Assert.Equal("60", dict["elk.layered.spacing.nodeNodeBetweenLayers"]);
    }

    [Fact]
    public void ToElkOptions_FormatsIntegersWithInvariantCulture()
    {
        // The Node-side ELK parser is locale-agnostic; if a non-en-US dev
        // machine ever formatted "1 000" the layout would silently fail.
        SchematicLayoutOptions options = new(
            EdgeRouting: "ORTHOGONAL", LayeredThoroughness: 1000,
            NodeNodeSpacing: 1000, NodeNodeSpacingBetweenLayers: 1000);

        Dictionary<string, string> dict = options.ToElkOptions();

        Assert.Equal("1000", dict["elk.layered.thoroughness"]);
        Assert.Equal("1000", dict["elk.spacing.nodeNode"]);
        Assert.Equal("1000", dict["elk.layered.spacing.nodeNodeBetweenLayers"]);
    }

    // ── Preset values ─────────────────────────────────────────────────────

    [Fact]
    public void For_FastPreview_PicksTheCheapestKnobs()
    {
        // Gate schematics must remain orthogonal at every quality level;
        // thoroughness 1 and tighter spacing provide the preview speed-up
        // without producing misleading diagonal connectivity.
        SchematicLayoutOptions options = ElkLayoutOptionsFactory.For(RoutingQuality.FastPreview);

        Assert.Equal("ORTHOGONAL", options.EdgeRouting);
        Assert.Equal(1, options.LayeredThoroughness);
        Assert.Equal(30, options.NodeNodeSpacing);
        Assert.Equal(50, options.NodeNodeSpacingBetweenLayers);
    }

    [Fact]
    public void For_Balanced_ReproducesTheWave4InlineValues()
    {
        // Pre-Wave 5 callers hard-coded these constants in
        // GateNetlistElkBuilder.BuildRootLayoutOptions. Balanced must stay
        // bit-for-bit identical so unspecified-preset callers don't regress.
        SchematicLayoutOptions options = ElkLayoutOptionsFactory.For(RoutingQuality.Balanced);

        Assert.Equal("ORTHOGONAL", options.EdgeRouting);
        Assert.Equal(3, options.LayeredThoroughness);
        Assert.Equal(40, options.NodeNodeSpacing);
        Assert.Equal(80, options.NodeNodeSpacingBetweenLayers);
    }

    [Fact]
    public void For_Production_TurnsEveryKnobUp()
    {
        // Production trades minutes for pixel quality; thoroughness 7 is
        // ELK's documented "max useful" iteration count.
        SchematicLayoutOptions options = ElkLayoutOptionsFactory.For(RoutingQuality.Production);

        Assert.Equal("ORTHOGONAL", options.EdgeRouting);
        Assert.Equal(7, options.LayeredThoroughness);
        Assert.Equal(50, options.NodeNodeSpacing);
        Assert.Equal(100, options.NodeNodeSpacingBetweenLayers);
    }

    [Theory]
    [InlineData(RoutingQuality.FastPreview)]
    [InlineData(RoutingQuality.Balanced)]
    [InlineData(RoutingQuality.Production)]
    public void For_EveryPreset_RoundTripsThroughToElkOptions(RoutingQuality quality)
    {
        // Every preset's structured form must successfully render to the ELK
        // dictionary — no missing/null keys.
        SchematicLayoutOptions options = ElkLayoutOptionsFactory.For(quality);
        Dictionary<string, string> dict = options.ToElkOptions();

        Assert.NotEmpty(dict["elk.edgeRouting"]);
        Assert.NotEmpty(dict["elk.layered.thoroughness"]);
        Assert.NotEmpty(dict["elk.spacing.nodeNode"]);
        Assert.NotEmpty(dict["elk.layered.spacing.nodeNodeBetweenLayers"]);
    }

    [Fact]
    public void For_UnknownPreset_ThrowsArgumentOutOfRange()
    {
        // Sanity guard so a newly added enum value can't silently fall through
        // and produce a half-configured layout.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ElkLayoutOptionsFactory.For((RoutingQuality)999));
    }

    // ── Ordering invariants (FastPreview < Balanced < Production by cost) ─

    [Fact]
    public void Presets_FormAMonotonicCostLadder()
    {
        // Thoroughness is the strongest knob; if a preset ever inverts the
        // order, the user picking "FastPreview" would get a slower layout.
        SchematicLayoutOptions fast = ElkLayoutOptionsFactory.For(RoutingQuality.FastPreview);
        SchematicLayoutOptions balanced = ElkLayoutOptionsFactory.For(RoutingQuality.Balanced);
        SchematicLayoutOptions production = ElkLayoutOptionsFactory.For(RoutingQuality.Production);

        Assert.True(fast.LayeredThoroughness <= balanced.LayeredThoroughness);
        Assert.True(balanced.LayeredThoroughness <= production.LayeredThoroughness);
    }
}
