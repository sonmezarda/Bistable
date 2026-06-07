using Bistable.Core.Projects;

namespace Bistable.App.Services.Routing.Elk;

public static class SchematicRoutingQualityResolver
{
    public const int DefaultLargeGraphThreshold = 1000;

    public static SchematicLayoutDecision Resolve(
        RoutingQuality requestedQuality,
        bool autoDowngradeLargeGraphs,
        int routableNodeCount,
        int largeGraphThreshold = DefaultLargeGraphThreshold)
    {
        if (largeGraphThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(largeGraphThreshold), largeGraphThreshold, "Threshold must be positive.");
        }

        bool shouldDowngrade = autoDowngradeLargeGraphs
            && routableNodeCount > largeGraphThreshold
            && requestedQuality != RoutingQuality.FastPreview;

        return shouldDowngrade
            ? new SchematicLayoutDecision(RoutingQuality.FastPreview, AutoDowngraded: true)
            : new SchematicLayoutDecision(requestedQuality, AutoDowngraded: false);
    }
}

public sealed record SchematicLayoutDecision(RoutingQuality EffectiveQuality, bool AutoDowngraded);
