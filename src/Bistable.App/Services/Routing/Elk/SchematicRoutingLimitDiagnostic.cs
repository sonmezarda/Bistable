namespace Bistable.App.Services.Routing.Elk;

public static class SchematicRoutingLimitDiagnostic
{
    public static string BuildMessage(
        string scopeName,
        SchematicGraphMetrics metrics,
        IReadOnlyCollection<string> expandedInstancePaths,
        bool hasHierarchicalChildren,
        bool isTopScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(expandedInstancePaths);

        string size = $"{metrics.NodeCount} nodes, {metrics.PortCount} ports, {metrics.EdgeCount} edges";

        if (expandedInstancePaths.Count > 0)
        {
            string expanded = string.Join(
                ", ",
                expandedInstancePaths.Order(StringComparer.Ordinal).Select(static path => $"'{path}'"));
            return $"Expanding {expanded} inside scope '{scopeName}' exceeds the full-routing safety limit ({size}). "
                + "Collapse one or more expanded modules (-) and open the required child scope separately.";
        }

        if (hasHierarchicalChildren)
        {
            return $"Scope '{scopeName}' exceeds the full-routing safety limit ({size}) even with hierarchy retained. "
                + "Open a smaller child module from the parent view instead of routing the complete scope.";
        }

        if (!isTopScope)
        {
            return $"Leaf scope '{scopeName}' exceeds the full-routing safety limit ({size}). "
                + "Re-synthesis will not reduce this leaf structure; use Up or the breadcrumb to return to its parent. "
                + "Detailed inspection requires a bounded logic-cone or macro view.";
        }

        return $"Top scope '{scopeName}' exceeds the full-routing safety limit ({size}) and has no retained child hierarchy. "
            + "Re-synthesize with Flatten disabled. If the result is unchanged, the design is a large leaf and requires "
            + "a bounded logic-cone or macro view.";
    }
}
