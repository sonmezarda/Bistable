using Bistable.Core.Projects;

namespace Bistable.App.Services.Routing.Elk;

public static class SchematicRoutingQualityResolver
{
    public const int DefaultLargeGraphThreshold = 1000;
    public const int DefaultLargePortThreshold = 1500;
    public const int DefaultLargeEdgeThreshold = 1000;
    // Absolute ELK safety limits, calibrated against real synthesized RV32
    // scopes. A hierarchy-preserved register-file expansion at
    // 5,572/20,196/14,056 completes under FastPreview (about 100 seconds on
    // the development machine), so it must remain available to the user.
    // The former flattened top at 13,233/47,381/33,906 still stays outside
    // these bounds and is rejected before it can wedge the router.
    public const int DefaultMonolithicNodeLimit = 10000;
    public const int DefaultMonolithicPortLimit = 40000;
    public const int DefaultMonolithicEdgeLimit = 30000;

    public static SchematicLayoutDecision Resolve(
        RoutingQuality requestedQuality,
        bool autoDowngradeLargeGraphs,
        int routableNodeCount,
        int largeGraphThreshold = DefaultLargeGraphThreshold)
        => Resolve(
            requestedQuality,
            autoDowngradeLargeGraphs,
            new SchematicGraphMetrics(routableNodeCount, PortCount: 0, EdgeCount: 0),
            largeGraphThreshold);

    public static SchematicLayoutDecision Resolve(
        RoutingQuality requestedQuality,
        bool autoDowngradeLargeGraphs,
        SchematicGraphMetrics metrics,
        int largeNodeThreshold = DefaultLargeGraphThreshold,
        int largePortThreshold = DefaultLargePortThreshold,
        int largeEdgeThreshold = DefaultLargeEdgeThreshold)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (largeNodeThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(largeNodeThreshold), largeNodeThreshold, "Threshold must be positive.");
        }
        if (largePortThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(largePortThreshold), largePortThreshold, "Threshold must be positive.");
        }
        if (largeEdgeThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(largeEdgeThreshold), largeEdgeThreshold, "Threshold must be positive.");
        }

        bool isLarge = metrics.NodeCount > largeNodeThreshold
            || metrics.PortCount > largePortThreshold
            || metrics.EdgeCount > largeEdgeThreshold;
        bool shouldDowngrade = autoDowngradeLargeGraphs
            && isLarge
            && requestedQuality != RoutingQuality.FastPreview;

        return shouldDowngrade
            ? new SchematicLayoutDecision(RoutingQuality.FastPreview, AutoDowngraded: true, metrics)
            : new SchematicLayoutDecision(requestedQuality, AutoDowngraded: false, metrics);
    }
}

public sealed record SchematicLayoutDecision(
    RoutingQuality EffectiveQuality,
    bool AutoDowngraded,
    SchematicGraphMetrics Metrics);

public sealed record SchematicGraphMetrics(int NodeCount, int PortCount, int EdgeCount)
{
    public bool RequiresExtendedRouting =>
        NodeCount > 5000
        || PortCount > 12000
        || EdgeCount > 10000;

    public bool ExceedsMonolithicRoutingLimit =>
        NodeCount > SchematicRoutingQualityResolver.DefaultMonolithicNodeLimit
        || PortCount > SchematicRoutingQualityResolver.DefaultMonolithicPortLimit
        || EdgeCount > SchematicRoutingQualityResolver.DefaultMonolithicEdgeLimit;

    public static SchematicGraphMetrics Measure(ElkGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        GraphCounts counts = CountNodes(graph.Children);
        return new SchematicGraphMetrics(
            counts.Nodes,
            counts.Ports,
            graph.Edges.Count + counts.NestedEdges);
    }

    private static GraphCounts CountNodes(IReadOnlyList<ElkNode>? nodes)
    {
        if (nodes is null)
        {
            return default;
        }

        GraphCounts result = default;
        foreach (ElkNode node in nodes)
        {
            result.Nodes++;
            result.Ports += node.Ports?.Count ?? 0;
            result.NestedEdges += node.Edges?.Count ?? 0;
            GraphCounts nested = CountNodes(node.Children);
            result.Nodes += nested.Nodes;
            result.Ports += nested.Ports;
            result.NestedEdges += nested.NestedEdges;
        }
        return result;
    }

    private struct GraphCounts
    {
        public int Nodes;
        public int Ports;
        public int NestedEdges;
    }
}
