using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;

namespace Bistable.Tests.Routing;

public sealed class SchematicRoutingQualityResolverTests
{
    [Fact]
    public void Resolve_BelowThreshold_KeepsRequestedQuality()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.Production,
            autoDowngradeLargeGraphs: true,
            routableNodeCount: 1000);

        Assert.Equal(RoutingQuality.Production, decision.EffectiveQuality);
        Assert.False(decision.AutoDowngraded);
    }

    [Fact]
    public void Resolve_AboveThreshold_DowngradesToFastPreview()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.Production,
            autoDowngradeLargeGraphs: true,
            routableNodeCount: 1001);

        Assert.Equal(RoutingQuality.FastPreview, decision.EffectiveQuality);
        Assert.True(decision.AutoDowngraded);
    }

    [Fact]
    public void Resolve_DisabledAutoDowngrade_KeepsRequestedQuality()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.Production,
            autoDowngradeLargeGraphs: false,
            routableNodeCount: 2000);

        Assert.Equal(RoutingQuality.Production, decision.EffectiveQuality);
        Assert.False(decision.AutoDowngraded);
    }

    [Fact]
    public void Resolve_FastPreviewRequest_DoesNotMarkAutoDowngrade()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.FastPreview,
            autoDowngradeLargeGraphs: true,
            routableNodeCount: 2000);

        Assert.Equal(RoutingQuality.FastPreview, decision.EffectiveQuality);
        Assert.False(decision.AutoDowngraded);
    }

    [Fact]
    public void Resolve_PortDenseGraph_DowngradesEvenWhenNodeCountIsSmall()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.Balanced,
            autoDowngradeLargeGraphs: true,
            new SchematicGraphMetrics(NodeCount: 179, PortCount: 2013, EdgeCount: 900));

        Assert.Equal(RoutingQuality.FastPreview, decision.EffectiveQuality);
        Assert.True(decision.AutoDowngraded);
    }

    [Fact]
    public void Resolve_EdgeDenseGraph_DowngradesEvenWhenNodeCountIsSmall()
    {
        SchematicLayoutDecision decision = SchematicRoutingQualityResolver.Resolve(
            RoutingQuality.Production,
            autoDowngradeLargeGraphs: true,
            new SchematicGraphMetrics(NodeCount: 200, PortCount: 500, EdgeCount: 1138));

        Assert.Equal(RoutingQuality.FastPreview, decision.EffectiveQuality);
        Assert.True(decision.AutoDowngraded);
    }

    [Fact]
    public void Metrics_Measure_CountsNestedNodesPortsAndEdges()
    {
        ElkGraph graph = new()
        {
            Edges = [new ElkEdge()],
            Children =
            [
                new ElkNode
                {
                    Ports = [new ElkPort(), new ElkPort()],
                    Edges = [new ElkEdge()],
                    Children =
                    [
                        new ElkNode
                        {
                            Ports = [new ElkPort()],
                            Edges = [new ElkEdge(), new ElkEdge()],
                        },
                    ],
                },
            ],
        };

        SchematicGraphMetrics metrics = SchematicGraphMetrics.Measure(graph);

        Assert.Equal(2, metrics.NodeCount);
        Assert.Equal(3, metrics.PortCount);
        Assert.Equal(4, metrics.EdgeCount);
    }

    [Theory]
    [InlineData(5001, 1, 1)]
    [InlineData(1, 12001, 1)]
    [InlineData(1, 1, 10001)]
    public void Metrics_ExceedsMonolithicRoutingLimit_ForAnySafetyDimension(
        int nodes,
        int ports,
        int edges)
    {
        SchematicGraphMetrics metrics = new(nodes, ports, edges);

        Assert.True(metrics.ExceedsMonolithicRoutingLimit);
    }
}
