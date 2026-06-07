namespace Bistable.Core.Projects;

/// <summary>
/// ELK routing-quality preset for gate-level schematics. Stored in the project
/// file because the right trade-off depends on design size and intent: a small
/// teaching design can use Production by default, while an RV32I core usually
/// wants FastPreview during exploration.
/// </summary>
public enum RoutingQuality
{
    /// <summary>Cheapest route: POLYLINE edges and minimum layered thoroughness.</summary>
    FastPreview,

    /// <summary>Default route: orthogonal edges with moderate layout cost.</summary>
    Balanced,

    /// <summary>Highest-quality route: orthogonal edges with maximum useful thoroughness.</summary>
    Production,
}

public sealed record SchematicConfiguration(
    RoutingQuality RoutingQuality = RoutingQuality.Balanced,
    bool AutoDowngradeLargeGraphs = true);
