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
}
