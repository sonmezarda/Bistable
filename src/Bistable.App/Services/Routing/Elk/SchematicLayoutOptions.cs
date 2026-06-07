using System.Globalization;
using Bistable.Core.Projects;

namespace Bistable.App.Services.Routing.Elk;

/// <summary>
/// Structured ELK layout knobs. Created from a <see cref="RoutingQuality"/>
/// preset via <see cref="ElkLayoutOptionsFactory.For"/>; converted to ELK's
/// string-keyed option dictionary via <see cref="ToElkOptions"/>. Routing
/// callers consume the dictionary form; tests pin the structured form so a
/// preset can't silently drift away from its documented values.
/// </summary>
/// <param name="EdgeRouting">ELK <c>elk.edgeRouting</c>. One of "ORTHOGONAL", "POLYLINE", "SPLINES".</param>
/// <param name="LayeredThoroughness">ELK <c>elk.layered.thoroughness</c>. Higher = more layout iterations = slower but cleaner.</param>
/// <param name="NodeNodeSpacing">ELK <c>elk.spacing.nodeNode</c>. Pixels between sibling cells.</param>
/// <param name="NodeNodeSpacingBetweenLayers">ELK <c>elk.layered.spacing.nodeNodeBetweenLayers</c>. Pixels between layout layers.</param>
public sealed record SchematicLayoutOptions(
    string EdgeRouting,
    int LayeredThoroughness,
    int NodeNodeSpacing,
    int NodeNodeSpacingBetweenLayers)
{
    /// <summary>
    /// Maps to the option dictionary ELK consumes. Kept narrow to the four
    /// knobs that actually move performance and visual density; algorithm and
    /// direction are fixed (we always want layered + RIGHT for schematics).
    /// </summary>
    public Dictionary<string, string> ToElkOptions() => new()
    {
        ["elk.algorithm"] = "layered",
        ["elk.direction"] = "RIGHT",
        ["elk.hierarchyHandling"] = "INCLUDE_CHILDREN",
        ["elk.edgeRouting"] = EdgeRouting,
        ["elk.layered.thoroughness"] = LayeredThoroughness.ToString(CultureInfo.InvariantCulture),
        ["elk.spacing.nodeNode"] = NodeNodeSpacing.ToString(CultureInfo.InvariantCulture),
        ["elk.layered.spacing.nodeNodeBetweenLayers"] = NodeNodeSpacingBetweenLayers.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Resolves a <see cref="RoutingQuality"/> preset to the structured options.
/// Single source of truth — every layout caller goes through here so the
/// presets stay consistent.
/// </summary>
public static class ElkLayoutOptionsFactory
{
    /// <summary>
    /// Numbers are tuned against arnicomp (small) and riscv_single_cycle (large)
    /// real designs, not synthetic graphs. Balanced reproduces the inline values
    /// that shipped through Wave 4 so behaviour is unchanged for callers that
    /// don't yet pass a preset.
    /// </summary>
    public static SchematicLayoutOptions For(RoutingQuality quality) => quality switch
    {
        RoutingQuality.FastPreview => new SchematicLayoutOptions(
            EdgeRouting: "POLYLINE",
            LayeredThoroughness: 1,
            NodeNodeSpacing: 30,
            NodeNodeSpacingBetweenLayers: 50),

        RoutingQuality.Balanced => new SchematicLayoutOptions(
            EdgeRouting: "ORTHOGONAL",
            LayeredThoroughness: 3,
            NodeNodeSpacing: 40,
            NodeNodeSpacingBetweenLayers: 80),

        RoutingQuality.Production => new SchematicLayoutOptions(
            EdgeRouting: "ORTHOGONAL",
            LayeredThoroughness: 7,
            NodeNodeSpacing: 50,
            NodeNodeSpacingBetweenLayers: 100),

        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown routing quality preset."),
    };
}
