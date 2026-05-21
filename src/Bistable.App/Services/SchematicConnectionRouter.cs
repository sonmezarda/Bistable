using Avalonia;

namespace Bistable.App.Services;

public sealed class SchematicConnectionRouter
{
    public IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input)
    {
        if (input.Requests.Count == 0)
        {
            return [];
        }

        return input.Layout.InlineChildren
            ? ComputeInlineRoutes(input)
            : ComputeStackedRoutes(input);
    }

    private static IReadOnlyList<SchematicConnectionRoute> ComputeInlineRoutes(SchematicConnectionRoutingInput input)
    {
        double laneMargin = input.CompactLayout ? 10 : 14;
        double leftLaneStart = input.Layout.CurrentNodeRect.Right + laneMargin;
        double leftLaneEnd = input.Layout.ChildNodeRects.Count == 0
            ? leftLaneStart + input.Layout.RouteCorridorWidth
            : input.Layout.ChildNodeRects.Min(static rect => rect.X) - laneMargin;
        double rightLaneStart = input.Layout.ChildNodeRects.Count == 0
            ? leftLaneEnd + laneMargin
            : input.Layout.ChildNodeRects.Max(static rect => rect.Right) + laneMargin;
        double rightLaneEnd = input.Layout.PanelRect.Right - (input.CompactLayout ? 18 : 24);

        List<SchematicConnectionRouteRequest> inputRoutes = input.Requests
            .Where(static route => route.TargetIsInput)
            .OrderBy(static route => route.Target.Y)
            .ThenBy(static route => route.Source.Y)
            .ToList();
        List<SchematicConnectionRouteRequest> outputRoutes = input.Requests
            .Where(static route => !route.TargetIsInput)
            .OrderBy(static route => route.Target.Y)
            .ThenBy(static route => route.Source.Y)
            .ToList();

        Dictionary<string, double> inputLanes = AssignBundleLanes(inputRoutes, leftLaneStart, leftLaneEnd);
        Dictionary<string, double> outputLanes = AssignBundleLanes(
            outputRoutes,
            Math.Max(rightLaneStart, leftLaneEnd + laneMargin),
            Math.Max(rightLaneStart, rightLaneEnd));
        Dictionary<string, SchematicConnectionBundle> bundles = BuildBundles(input.Requests);

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicConnectionRouteRequest request in input.Requests)
        {
            double laneX = request.TargetIsInput
                ? inputLanes[request.BundleKey]
                : outputLanes[request.BundleKey];
            SchematicConnectionBundle bundle = bundles[request.BundleKey];
            if (request.TargetIsInput)
            {
                Point elbow1 = new(laneX, request.Source.Y);
                Point elbow2 = new(laneX, request.Target.Y);
                IReadOnlyList<Point> points = AvoidObstacles(
                    [request.Source, elbow1, elbow2, request.Target],
                    input,
                    request);
                routes.Add(new SchematicConnectionRoute(
                    request.Id,
                    request.BundleKey,
                    bundle.Size,
                    string.Equals(bundle.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                    points,
                    BuildLabelBounds(new Point(laneX, bundle.CenterY), request.LabelWidth),
                    new Point(laneX, (request.Source.Y + request.Target.Y) / 2)));
                continue;
            }

            double bridgeY = request.SourceFromLocalSignal
                ? Math.Max(
                    input.Layout.ChildNodeRects.Count == 0 ? request.Source.Y : input.Layout.ChildNodeRects.Max(static rect => rect.Bottom) + (input.CompactLayout ? 16 : 22),
                    request.Source.Y)
                : Math.Min(input.Layout.CurrentNodeRect.Y, input.Layout.ChildNodeRects.Count == 0 ? input.Layout.CurrentNodeRect.Y : input.Layout.ChildNodeRects.Min(static rect => rect.Y)) - (input.CompactLayout ? 18 : 24);
            bridgeY += bundle.LaneOffset;
            double corridorX = Math.Min(laneX - laneMargin, input.Layout.CurrentNodeRect.Right + input.Layout.RouteCorridorWidth * 0.38);
            IReadOnlyList<Point> outputPoints = AvoidObstacles(
                [
                    request.Source,
                    new Point(corridorX, request.Source.Y),
                    new Point(corridorX, bridgeY),
                    new Point(laneX, bridgeY),
                    new Point(laneX, request.Target.Y),
                    request.Target
                ],
                input,
                request);
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                request.BundleKey,
                bundle.Size,
                string.Equals(bundle.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                outputPoints,
                BuildLabelBounds(new Point(laneX, bridgeY), request.LabelWidth),
                new Point(laneX, bridgeY)));
        }

        return PlaceLabels(routes, input.Layout.PanelRect, input.CompactLayout, input.Obstacles ?? []);
    }

    private static IReadOnlyList<SchematicConnectionRoute> ComputeStackedRoutes(SchematicConnectionRoutingInput input)
    {
        Rect currentRect = input.Layout.CurrentNodeRect;
        double upperLaneStart = currentRect.Bottom + (input.CompactLayout ? 10 : 14);
        double upperLaneEnd = input.Layout.ChildNodeRects.Count == 0
            ? upperLaneStart + (input.CompactLayout ? 24 : 32)
            : input.Layout.ChildNodeRects.Min(static rect => rect.Y) - (input.CompactLayout ? 10 : 14);
        double lowerLaneStart = input.Layout.ChildNodeRects.Count == 0
            ? upperLaneEnd + (input.CompactLayout ? 12 : 16)
            : input.Layout.ChildNodeRects.Max(static rect => rect.Bottom) + (input.CompactLayout ? 10 : 14);
        double lowerLaneEnd = input.Layout.LocalSectionRect?.Y is double localTop
            ? localTop - (input.CompactLayout ? 8 : 12)
            : lowerLaneStart + (input.CompactLayout ? 24 : 32);

        List<SchematicConnectionRouteRequest> currentRoutes = input.Requests
            .Where(static route => !route.SourceFromLocalSignal)
            .OrderBy(static route => route.Target.X)
            .ThenBy(static route => route.Target.Y)
            .ToList();
        List<SchematicConnectionRouteRequest> localRoutes = input.Requests
            .Where(static route => route.SourceFromLocalSignal)
            .OrderBy(static route => route.Source.Y)
            .ThenBy(static route => route.Target.X)
            .ToList();

        Dictionary<string, double> currentLanes = AssignBundleLanes(currentRoutes, upperLaneStart, Math.Max(upperLaneStart, upperLaneEnd));
        Dictionary<string, double> localLanes = AssignBundleLanes(localRoutes, lowerLaneStart, Math.Max(lowerLaneStart, lowerLaneEnd));
        Dictionary<string, SchematicConnectionBundle> bundles = BuildBundles(input.Requests);

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicConnectionRouteRequest request in input.Requests)
        {
            double laneY = request.SourceFromLocalSignal
                ? localLanes[request.BundleKey]
                : currentLanes[request.BundleKey];
            SchematicConnectionBundle bundle = bundles[request.BundleKey];
            Point elbow1 = new(request.Source.X, laneY);
            Point elbow2 = new(request.Target.X, laneY);
            IReadOnlyList<Point> points = AvoidObstacles(
                [request.Source, elbow1, elbow2, request.Target],
                input,
                request);
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                request.BundleKey,
                bundle.Size,
                string.Equals(bundle.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                points,
                BuildLabelBounds(new Point(bundle.CenterX, laneY), request.LabelWidth),
                new Point((request.Source.X + request.Target.X) / 2, laneY)));
        }

        return PlaceLabels(routes, input.Layout.PanelRect, input.CompactLayout, input.Obstacles ?? []);
    }

    private static IReadOnlyList<Point> AvoidObstacles(
        IReadOnlyList<Point> points,
        SchematicConnectionRoutingInput input,
        SchematicConnectionRouteRequest request)
    {
        IReadOnlyList<Rect> obstacles = input.Obstacles ?? [];
        if (obstacles.Count == 0 || points.Count < 2)
        {
            return points;
        }

        double padding = input.CompactLayout ? 8 : 10;
        IReadOnlyList<Point> routed = points;
        for (int pass = 0; pass < 5; pass++)
        {
            bool changed = false;
            List<Point> next = [routed[0]];
            for (int index = 0; index < routed.Count - 1; index++)
            {
                Point start = next[^1];
                Point end = routed[index + 1];
                IReadOnlyList<Point> segment = RouteSegmentAroundObstacles(start, end, obstacles, input.Layout.PanelRect, padding, request);
                changed |= segment.Count > 2;
                for (int pointIndex = 1; pointIndex < segment.Count; pointIndex++)
                {
                    AddDistinctPoint(next, segment[pointIndex]);
                }
            }

            routed = next;
            if (!changed)
            {
                break;
            }
        }

        return routed.Count == points.Count && routed.SequenceEqual(points)
            ? points
            : routed;
    }

    private static IReadOnlyList<Point> RouteSegmentAroundObstacles(
        Point start,
        Point end,
        IReadOnlyList<Rect> obstacles,
        Rect bounds,
        double padding,
        SchematicConnectionRouteRequest request)
    {
        Rect? obstacle = FindBlockingObstacle(start, end, obstacles, padding);
        if (obstacle is null)
        {
            return [start, end];
        }

        Rect inflated = obstacle.Value.Inflate(padding);
        if (IsHorizontal(start, end))
        {
            double detourY = ChooseHorizontalDetourY(start, end, inflated, bounds, request);
            return
            [
                start,
                new Point(start.X, detourY),
                new Point(end.X, detourY),
                end
            ];
        }

        if (IsVertical(start, end))
        {
            double detourX = ChooseVerticalDetourX(start, end, inflated, bounds, request);
            return
            [
                start,
                new Point(detourX, start.Y),
                new Point(detourX, end.Y),
                end
            ];
        }

        return [start, end];
    }

    private static Rect? FindBlockingObstacle(Point start, Point end, IReadOnlyList<Rect> obstacles, double padding)
    {
        foreach (Rect obstacle in obstacles)
        {
            Rect inflated = obstacle.Inflate(padding);
            if (inflated.Contains(start) || inflated.Contains(end))
            {
                continue;
            }

            if (IsHorizontal(start, end)
                && start.Y > inflated.Y
                && start.Y < inflated.Bottom
                && RangesOverlap(start.X, end.X, inflated.X, inflated.Right))
            {
                return obstacle;
            }

            if (IsVertical(start, end)
                && start.X > inflated.X
                && start.X < inflated.Right
                && RangesOverlap(start.Y, end.Y, inflated.Y, inflated.Bottom))
            {
                return obstacle;
            }
        }

        return null;
    }

    private static double ChooseHorizontalDetourY(Point start, Point end, Rect obstacle, Rect bounds, SchematicConnectionRouteRequest request)
    {
        double margin = 12;
        double above = obstacle.Y - margin;
        double below = obstacle.Bottom + margin;
        double minY = bounds.Y + margin;
        double maxY = bounds.Bottom - margin;
        bool preferAbove = !request.SourceFromLocalSignal && (start.Y + end.Y) / 2 <= obstacle.Center.Y;
        double preferred = preferAbove ? above : below;
        double alternate = preferAbove ? below : above;

        if (preferred >= minY && preferred <= maxY)
        {
            return preferred;
        }

        return Math.Clamp(alternate, minY, maxY);
    }

    private static double ChooseVerticalDetourX(Point start, Point end, Rect obstacle, Rect bounds, SchematicConnectionRouteRequest request)
    {
        double margin = 12;
        double left = obstacle.X - margin;
        double right = obstacle.Right + margin;
        double minX = bounds.X + margin;
        double maxX = bounds.Right - margin;
        bool preferRight = !request.TargetIsInput || (start.X + end.X) / 2 >= obstacle.Center.X;
        double preferred = preferRight ? right : left;
        double alternate = preferRight ? left : right;

        if (preferred >= minX && preferred <= maxX)
        {
            return preferred;
        }

        return Math.Clamp(alternate, minX, maxX);
    }

    private static bool IsHorizontal(Point start, Point end) => Math.Abs(start.Y - end.Y) < 0.01;

    private static bool IsVertical(Point start, Point end) => Math.Abs(start.X - end.X) < 0.01;

    private static bool RangesOverlap(double a1, double a2, double b1, double b2)
    {
        double minA = Math.Min(a1, a2);
        double maxA = Math.Max(a1, a2);
        return maxA > b1 && minA < b2;
    }

    private static void AddDistinctPoint(List<Point> points, Point point)
    {
        if (points.Count == 0 || Distance(points[^1], point) > 0.01)
        {
            points.Add(point);
        }
    }

    private static double Distance(Point first, Point second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static Dictionary<string, double> AssignBundleLanes(
        IReadOnlyList<SchematicConnectionRouteRequest> requests,
        double start,
        double end)
    {
        Dictionary<string, double> lanes = new(StringComparer.OrdinalIgnoreCase);
        if (requests.Count == 0)
        {
            return lanes;
        }

        List<IGrouping<string, SchematicConnectionRouteRequest>> bundles = requests
            .GroupBy(static request => request.BundleKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static bundle => bundle.Average(static request => request.Target.Y))
            .ThenBy(static bundle => bundle.Average(static request => request.Target.X))
            .ThenBy(static bundle => bundle.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bundles.Count == 1 || Math.Abs(end - start) < 1)
        {
            double mid = (start + end) / 2;
            foreach (IGrouping<string, SchematicConnectionRouteRequest> bundle in bundles)
            {
                lanes[bundle.Key] = mid;
            }

            return lanes;
        }

        double step = (end - start) / (bundles.Count + 1);
        for (int index = 0; index < bundles.Count; index++)
        {
            lanes[bundles[index].Key] = start + step * (index + 1);
        }

        return lanes;
    }

    private static Dictionary<string, SchematicConnectionBundle> BuildBundles(IReadOnlyList<SchematicConnectionRouteRequest> requests)
    {
        Dictionary<string, SchematicConnectionBundle> bundles = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SchematicConnectionRouteRequest> group in requests.GroupBy(static request => request.BundleKey, StringComparer.OrdinalIgnoreCase))
        {
            SchematicConnectionRouteRequest[] ordered = group
                .OrderBy(static request => request.Target.Y)
                .ThenBy(static request => request.Target.X)
                .ThenBy(static request => request.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            double centerX = ordered.Average(static request => (request.Source.X + request.Target.X) / 2);
            double centerY = ordered.Average(static request => (request.Source.Y + request.Target.Y) / 2);
            double laneOffset = ordered.Length <= 1
                ? 0
                : Math.Clamp((ordered[0].Target.Y - centerY) * 0.12, -12, 12);
            bundles[group.Key] = new SchematicConnectionBundle(group.Key, ordered[0].Id, ordered.Length, centerX, centerY, laneOffset);
        }

        return bundles;
    }

    private static Rect BuildLabelBounds(Point anchor, int width)
    {
        double labelWidth = width <= 1 ? 30 : Math.Clamp(24 + width * 2.4, 36, 72);
        return new Rect(anchor.X - labelWidth / 2, anchor.Y - 9, labelWidth, 18);
    }

    private static IReadOnlyList<SchematicConnectionRoute> PlaceLabels(
        IReadOnlyList<SchematicConnectionRoute> routes,
        Rect bounds,
        bool compactLayout,
        IReadOnlyList<Rect> obstacles)
    {
        if (routes.Count <= 1)
        {
            if (routes.Count == 0)
            {
                return routes;
            }

            SchematicConnectionRoute single = routes[0];
            Rect placedSingle = PlaceLabel(single.LabelBounds, bounds, compactLayout, [], obstacles);
            return [single with { LabelBounds = placedSingle }];
        }

        List<Rect> placed = [];
        Dictionary<string, Rect> labels = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchematicConnectionRoute route in routes
            .Where(static route => route.IsBundlePrimary)
            .OrderBy(static route => route.LabelBounds.Y)
            .ThenBy(static route => route.LabelBounds.X))
        {
            Rect label = PlaceLabel(route.LabelBounds, bounds, compactLayout, placed, obstacles);
            placed.Add(label);
            labels[route.Id] = label;
        }

        return routes
            .Select(route => route with { LabelBounds = labels.TryGetValue(route.Id, out Rect label) ? label : route.LabelBounds })
            .ToList();
    }

    private static Rect PlaceLabel(
        Rect preferredLabel,
        Rect bounds,
        bool compactLayout,
        IReadOnlyList<Rect> placedLabels,
        IReadOnlyList<Rect> obstacles)
    {
        double margin = compactLayout ? 10 : 14;
        double verticalStep = compactLayout ? 20 : 22;
        double horizontalStep = compactLayout ? 30 : 38;
        double minX = bounds.X + margin;
        double maxX = Math.Max(minX, bounds.Right - margin);
        double minY = bounds.Y + margin;
        double maxY = Math.Max(minY, bounds.Bottom - margin);

        foreach (Rect candidate in EnumerateLabelCandidates(preferredLabel, verticalStep, horizontalStep)
            .Select(candidate => ClampLabel(candidate, minX, maxX, minY, maxY)))
        {
            if (!IsLabelBlocked(candidate, placedLabels, obstacles))
            {
                return candidate;
            }
        }

        return ClampLabel(preferredLabel, minX, maxX, minY, maxY);
    }

    private static IEnumerable<Rect> EnumerateLabelCandidates(Rect preferredLabel, double verticalStep, double horizontalStep)
    {
        yield return preferredLabel;

        for (int ring = 1; ring <= 16; ring++)
        {
            double dy = ring * verticalStep;
            yield return new Rect(preferredLabel.X, preferredLabel.Y + dy, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X, preferredLabel.Y - dy, preferredLabel.Width, preferredLabel.Height);
        }

        for (int ring = 1; ring <= 10; ring++)
        {
            double dx = ring * horizontalStep;
            yield return new Rect(preferredLabel.X + dx, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X - dx, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
        }

        for (int ring = 1; ring <= 8; ring++)
        {
            double dx = ring * horizontalStep;
            double dy = ring * verticalStep;
            yield return new Rect(preferredLabel.X + dx, preferredLabel.Y + dy, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X - dx, preferredLabel.Y + dy, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X + dx, preferredLabel.Y - dy, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X - dx, preferredLabel.Y - dy, preferredLabel.Width, preferredLabel.Height);
        }
    }

    private static bool IsLabelBlocked(Rect label, IReadOnlyList<Rect> placedLabels, IReadOnlyList<Rect> obstacles)
    {
        if (placedLabels.Any(candidate => candidate.Inflate(2).Intersects(label)))
        {
            return true;
        }

        return obstacles.Any(obstacle => obstacle.Inflate(3).Intersects(label));
    }

    private static Rect ClampLabel(Rect label, double minX, double maxX, double minY, double maxY)
    {
        double x = Math.Clamp(label.X, minX, Math.Max(minX, maxX - label.Width));
        double y = Math.Clamp(label.Y, minY, Math.Max(minY, maxY - label.Height));
        return new Rect(x, y, label.Width, label.Height);
    }
}

public sealed record SchematicConnectionRoutingInput(
    SchematicScopePanelLayout Layout,
    bool CompactLayout,
    IReadOnlyList<SchematicConnectionRouteRequest> Requests,
    IReadOnlyList<Rect>? Obstacles = null);

public sealed record SchematicConnectionRouteRequest(
    string Id,
    string SignalName,
    string? SelectionSignalName,
    int LabelWidth,
    Point Source,
    Point Target,
    bool SourceFromLocalSignal,
    bool TargetIsInput)
{
    public string BundleKey => string.IsNullOrWhiteSpace(SelectionSignalName) ? SignalName : SelectionSignalName;
}

public sealed record SchematicConnectionRoute(
    string Id,
    string BundleKey,
    int BundleSize,
    bool IsBundlePrimary,
    IReadOnlyList<Point> Points,
    Rect LabelBounds,
    Point LabelAnchor);

internal sealed record SchematicConnectionBundle(
    string Key,
    string PrimaryRequestId,
    int Size,
    double CenterX,
    double CenterY,
    double LaneOffset);
