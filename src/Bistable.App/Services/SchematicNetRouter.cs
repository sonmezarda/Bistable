using Avalonia;

namespace Bistable.App.Services;

public interface ISchematicRouter
{
    IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input);
}

public interface ISchematicLayoutEngine
{
    SchematicScopePanelLayout Compute(SchematicScopeLayoutInput input);
}

public sealed class SchematicGraphBuilder
{
    public SchematicGraph Build(SchematicConnectionRoutingInput input)
    {
        IReadOnlyList<SchematicNet> nets = input.Requests
            .GroupBy(static request => request.BundleKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new SchematicNet(group.Key, group.OrderBy(static request => request.Id, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(static net => net.Fanout)
            .ThenBy(static net => net.PrimaryKind)
            .ThenBy(static net => net.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SchematicGraph(nets, input.Obstacles ?? []);
    }
}

public sealed class GridSchematicRouter : ISchematicRouter
{
    private const double MinimumLaneSpacing = 18;
    private readonly SchematicGraphBuilder _graphBuilder = new();

    public IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input)
    {
        if (input.Requests.Count == 0)
        {
            return [];
        }

        SchematicGraph graph = _graphBuilder.Build(input);
        Dictionary<string, int> netOrder = graph.Nets
            .Select((net, index) => (net, index))
            .ToDictionary(static item => item.net.Key, static item => item.index, StringComparer.OrdinalIgnoreCase);

        RoutedLaneSet laneSet = input.Layout.InlineChildren
            ? BuildInlineLaneSet(input, graph)
            : BuildStackedLaneSet(input, graph);

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicNet net in graph.Nets)
        {
            double lane = laneSet.GetLane(net);
            foreach (SchematicConnectionRouteRequest request in net.Requests)
            {
                IReadOnlyList<Point> rawPoints = input.Layout.InlineChildren
                    ? BuildInlineRoute(input, request, lane)
                    : BuildStackedRoute(input, request, lane);
                IReadOnlyList<Point> points = NormalizeOrthogonalPath(AvoidObstacles(rawPoints, input, request, netOrder[net.Key]));
                routes.Add(new SchematicConnectionRoute(
                    request.Id,
                    request.BundleKey,
                    net.Fanout,
                    string.Equals(net.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                    points,
                    BuildLabelBounds(points, request.LabelWidth),
                    GetLabelAnchor(points),
                    Junctions: []));
            }
        }

        IReadOnlyList<SchematicConnectionRoute> withJunctions = AddJunctions(routes);
        IReadOnlyList<SchematicConnectionRoute> withBridges = AddBridgeMetadata(withJunctions);
        return PlaceLabels(withBridges, input.Layout.PanelRect, input.CompactLayout, graph.Obstacles);
    }

    private static RoutedLaneSet BuildInlineLaneSet(SchematicConnectionRoutingInput input, SchematicGraph graph)
    {
        double margin = input.CompactLayout ? 12 : 16;
        double childLeft = input.Layout.ChildNodeRects.Count == 0
            ? input.Layout.CurrentNodeRect.Right + input.Layout.RouteCorridorWidth
            : input.Layout.ChildNodeRects.Min(static rect => rect.X);
        double childRight = input.Layout.ChildNodeRects.Count == 0
            ? childLeft
            : input.Layout.ChildNodeRects.Max(static rect => rect.Right);
        double inputStart = graph.Nets
            .SelectMany(static net => net.Requests)
            .Where(static request => request.Kind == SchematicConnectionRouteKind.BoundaryToChildInput)
            .Select(static request => request.Source.X)
            .DefaultIfEmpty(input.Layout.CurrentNodeRect.Right)
            .Max() + margin;
        double inputEnd = childLeft - margin;
        double outputStart = childRight + margin;
        double outputTargetLeft = graph.Nets
            .SelectMany(static net => net.Requests)
            .Where(static request => request.Kind == SchematicConnectionRouteKind.ChildOutputToBoundary)
            .Select(static request => request.Target.X)
            .DefaultIfEmpty(input.Layout.PanelRect.Right - margin)
            .Min();
        double outputEnd = Math.Min(input.Layout.PanelRect.Right - margin, outputTargetLeft - margin);
        double childBottom = input.Layout.ChildNodeRects.Count == 0
            ? input.Layout.CurrentNodeRect.Bottom
            : input.Layout.ChildNodeRects.Max(static rect => rect.Bottom);
        double localStart = childBottom + margin;
        double localEnd = (input.Layout.LocalSectionRect?.Y ?? input.Layout.PanelRect.Bottom - margin) - margin;

        return new RoutedLaneSet(
            AssignLanes(graph.Nets.Where(static net => net.PrimaryKind == SchematicConnectionRouteKind.BoundaryToChildInput), inputStart, inputEnd),
            AssignLanes(graph.Nets.Where(static net => net.PrimaryKind == SchematicConnectionRouteKind.ChildOutputToBoundary), outputStart, outputEnd),
            AssignLanes(graph.Nets.Where(static net => net.PrimaryKind is SchematicConnectionRouteKind.ChildOutputToLocal
                or SchematicConnectionRouteKind.LocalToChildInput
                or SchematicConnectionRouteKind.ChildOutputToChildInput), localStart, localEnd));
    }

    private static RoutedLaneSet BuildStackedLaneSet(SchematicConnectionRoutingInput input, SchematicGraph graph)
    {
        double margin = input.CompactLayout ? 12 : 16;
        double upperStart = input.Layout.CurrentNodeRect.Bottom + margin;
        double upperEnd = input.Layout.ChildNodeRects.Count == 0
            ? upperStart + (input.CompactLayout ? 44 : 56)
            : input.Layout.ChildNodeRects.Min(static rect => rect.Y) - margin;
        double lowerStart = input.Layout.ChildNodeRects.Count == 0
            ? upperEnd + margin
            : input.Layout.ChildNodeRects.Max(static rect => rect.Bottom) + margin;
        double lowerEnd = (input.Layout.LocalSectionRect?.Y ?? input.Layout.PanelRect.Bottom - margin) - margin;

        IEnumerable<SchematicNet> currentNets = graph.Nets
            .Where(static net => net.PrimaryKind is SchematicConnectionRouteKind.BoundaryToChildInput or SchematicConnectionRouteKind.ChildOutputToBoundary);
        IEnumerable<SchematicNet> localNets = graph.Nets
            .Where(static net => net.PrimaryKind is SchematicConnectionRouteKind.ChildOutputToLocal
                or SchematicConnectionRouteKind.LocalToChildInput
                or SchematicConnectionRouteKind.ChildOutputToChildInput);

        return new RoutedLaneSet(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), AssignLanes(currentNets, upperStart, upperEnd).Concat(AssignLanes(localNets, lowerStart, lowerEnd))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, double> AssignLanes(IEnumerable<SchematicNet> nets, double start, double end)
    {
        SchematicNet[] ordered = nets
            .OrderBy(static net => net.AverageTarget)
            .ThenBy(static net => net.AverageSource)
            .ThenBy(static net => net.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, double> lanes = new(StringComparer.OrdinalIgnoreCase);
        if (ordered.Length == 0)
        {
            return lanes;
        }

        double low = Math.Min(start, end);
        double high = Math.Max(start, end);
        double available = Math.Max(0, high - low);
        double step = ordered.Length == 1
            ? 0
            : Math.Max(MinimumLaneSpacing, available / Math.Max(1, ordered.Length - 1));
        double first = ordered.Length == 1
            ? (low + high) / 2
            : low;
        for (int index = 0; index < ordered.Length; index++)
        {
            lanes[ordered[index].Key] = Math.Clamp(first + step * index, low, high);
        }

        return lanes;
    }

    private static IReadOnlyList<Point> BuildInlineRoute(
        SchematicConnectionRoutingInput input,
        SchematicConnectionRouteRequest request,
        double lane)
    {
        if (request.RoutesToChildInput && !request.UsesLocalNet)
        {
            return [request.Source, new Point(lane, request.Source.Y), new Point(lane, request.Target.Y), request.Target];
        }

        if (request.RoutesFromChildOutput && !request.UsesLocalNet)
        {
            return [request.Source, new Point(lane, request.Source.Y), new Point(lane, request.Target.Y), request.Target];
        }

        return request.Kind == SchematicConnectionRouteKind.ChildOutputToChildInput
            ? BuildPeerLocalRoute(input, request)
            : BuildLocalRoute(request, lane, input.CompactLayout);
    }

    private static IReadOnlyList<Point> BuildStackedRoute(
        SchematicConnectionRoutingInput input,
        SchematicConnectionRouteRequest request,
        double lane)
    {
        return request.UsesLocalNet
            ? request.Kind == SchematicConnectionRouteKind.ChildOutputToChildInput
                ? BuildPeerLocalRoute(input, request)
                : BuildLocalRoute(request, lane, input.CompactLayout)
            : [request.Source, new Point(request.Source.X, lane), new Point(request.Target.X, lane), request.Target];
    }

    private static IReadOnlyList<Point> BuildPeerLocalRoute(SchematicConnectionRoutingInput input, SchematicConnectionRouteRequest request)
    {
        double stub = input.CompactLayout ? 32 : 44;
        double rightCorridor = Math.Min(
            input.Layout.PanelRect.Right - (input.CompactLayout ? 28 : 36),
            Math.Max(request.Source.X, request.Target.X) + stub);
        if (rightCorridor <= request.Source.X + 4)
        {
            rightCorridor = request.Source.X + stub;
        }

        double targetApproach = request.Target.X - stub;
        double laneY = ChoosePeerLaneY(input.Layout.ChildNodeRects, request.Source, request.Target);
        return
        [
            request.Source,
            new Point(rightCorridor, request.Source.Y),
            new Point(rightCorridor, laneY),
            new Point(targetApproach, laneY),
            new Point(targetApproach, request.Target.Y),
            request.Target
        ];
    }

    private static double ChoosePeerLaneY(IReadOnlyList<Rect> childRects, Point source, Point target)
    {
        Rect? sourceRect = FindContainingRect(childRects, source);
        Rect? targetRect = FindContainingRect(childRects, target);
        if (sourceRect is { } sourceBounds && targetRect is { } targetBounds)
        {
            if (sourceBounds.Bottom <= targetBounds.Y)
            {
                return (sourceBounds.Bottom + targetBounds.Y) / 2;
            }

            if (targetBounds.Bottom <= sourceBounds.Y)
            {
                return (targetBounds.Bottom + sourceBounds.Y) / 2;
            }
        }

        return (source.Y + target.Y) / 2;
    }

    private static Rect? FindContainingRect(IReadOnlyList<Rect> rects, Point point)
    {
        foreach (Rect rect in rects)
        {
            if (rect.Inflate(1).Contains(point))
            {
                return rect;
            }
        }

        return null;
    }

    private static IReadOnlyList<Point> BuildLocalRoute(SchematicConnectionRouteRequest request, double laneY, bool compactLayout)
    {
        double stub = compactLayout ? 30 : 40;
        double sourceDirection = request.RoutesFromChildOutput
            ? 1
            : request.Target.X >= request.Source.X ? 1 : -1;
        double targetDirection = request.RoutesToChildInput
            ? -1
            : request.Target.X >= request.Source.X ? -1 : 1;
        Point sourceExit = new(request.Source.X + sourceDirection * stub, request.Source.Y);
        Point targetEntry = new(request.Target.X + targetDirection * stub, request.Target.Y);
        return [request.Source, sourceExit, new Point(sourceExit.X, laneY), new Point(targetEntry.X, laneY), targetEntry, request.Target];
    }

    private static IReadOnlyList<Point> AvoidObstacles(
        IReadOnlyList<Point> points,
        SchematicConnectionRoutingInput input,
        SchematicConnectionRouteRequest request,
        int netIndex)
    {
        IReadOnlyList<Rect> obstacles = input.Obstacles ?? [];
        if (obstacles.Count == 0 || points.Count < 2)
        {
            return points;
        }

        double padding = input.CompactLayout ? 8 : 10;
        IReadOnlyList<Point> routed = points;
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            List<Point> next = [routed[0]];
            for (int index = 0; index < routed.Count - 1; index++)
            {
                Point start = next[^1];
                Point end = routed[index + 1];
                IReadOnlyList<Point> segment = RouteSegmentAroundObstacles(start, end, obstacles, input.Layout.PanelRect, padding, request, netIndex);
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

        return routed;
    }

    private static IReadOnlyList<Point> RouteSegmentAroundObstacles(
        Point start,
        Point end,
        IReadOnlyList<Rect> obstacles,
        Rect bounds,
        double padding,
        SchematicConnectionRouteRequest request,
        int netIndex)
    {
        Rect? obstacle = FindBlockingObstacle(start, end, obstacles, padding);
        if (obstacle is null)
        {
            return [start, end];
        }

        Rect inflated = obstacle.Value.Inflate(padding);
        double detourSpacing = 4 * (netIndex % 7);
        if (IsHorizontal(start, end))
        {
            double detourY = ChooseHorizontalDetourY(start, end, inflated, bounds, request, detourSpacing);
            return [start, new Point(start.X, detourY), new Point(end.X, detourY), end];
        }

        if (IsVertical(start, end))
        {
            double detourX = ChooseVerticalDetourX(start, end, inflated, bounds, request, detourSpacing);
            return [start, new Point(detourX, start.Y), new Point(detourX, end.Y), end];
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

    private static double ChooseHorizontalDetourY(Point start, Point end, Rect obstacle, Rect bounds, SchematicConnectionRouteRequest request, double offset)
    {
        double margin = 12 + offset;
        double above = obstacle.Y - margin;
        double below = obstacle.Bottom + margin;
        bool preferAbove = !request.UsesLocalNet && (start.Y + end.Y) / 2 <= obstacle.Center.Y;
        double preferred = preferAbove ? above : below;
        double alternate = preferAbove ? below : above;
        double minY = bounds.Y + 12;
        double maxY = bounds.Bottom - 12;
        return preferred >= minY && preferred <= maxY
            ? preferred
            : Math.Clamp(alternate, minY, maxY);
    }

    private static double ChooseVerticalDetourX(Point start, Point end, Rect obstacle, Rect bounds, SchematicConnectionRouteRequest request, double offset)
    {
        double margin = 12 + offset;
        double left = obstacle.X - margin;
        double right = obstacle.Right + margin;
        bool preferRight = request.RoutesFromChildOutput || (start.X + end.X) / 2 >= obstacle.Center.X;
        double preferred = preferRight ? right : left;
        double alternate = preferRight ? left : right;
        double minX = bounds.X + 12;
        double maxX = bounds.Right - 12;
        return preferred >= minX && preferred <= maxX
            ? preferred
            : Math.Clamp(alternate, minX, maxX);
    }

    private static IReadOnlyList<Point> NormalizeOrthogonalPath(IReadOnlyList<Point> points)
    {
        List<Point> normalized = [];
        foreach (Point point in points)
        {
            AddDistinctPoint(normalized, point);
        }

        for (int index = normalized.Count - 2; index > 0; index--)
        {
            Point previous = normalized[index - 1];
            Point current = normalized[index];
            Point next = normalized[index + 1];
            if ((IsHorizontal(previous, current) && IsHorizontal(current, next))
                || (IsVertical(previous, current) && IsVertical(current, next)))
            {
                normalized.RemoveAt(index);
            }
        }

        return normalized;
    }

    private static IReadOnlyList<SchematicConnectionRoute> AddJunctions(IReadOnlyList<SchematicConnectionRoute> routes)
    {
        Dictionary<string, HashSet<PointKey>> junctionsByNet = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SchematicConnectionRoute> netRoutes in routes.GroupBy(static route => route.BundleKey, StringComparer.OrdinalIgnoreCase))
        {
            Dictionary<PointKey, int> counts = [];
            foreach (SchematicConnectionRoute route in netRoutes)
            {
                foreach (Point point in route.Points.Skip(1).SkipLast(1))
                {
                    PointKey key = PointKey.From(point);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }

            junctionsByNet[netRoutes.Key] = counts
                .Where(static item => item.Value > 1)
                .Select(static item => item.Key)
                .ToHashSet();
        }

        return routes
            .Select(route => route with
            {
                Junctions = route.Points
                    .Where(point => junctionsByNet.TryGetValue(route.BundleKey, out HashSet<PointKey>? keys) && keys.Contains(PointKey.From(point)))
                    .Distinct()
                    .ToArray()
            })
            .ToArray();
    }

    private static IReadOnlyList<SchematicConnectionRoute> AddBridgeMetadata(IReadOnlyList<SchematicConnectionRoute> routes)
    {
        List<List<SchematicRouteBridge>> bridges = routes.Select(static _ => new List<SchematicRouteBridge>()).ToList();
        for (int first = 0; first < routes.Count; first++)
        {
            for (int second = first + 1; second < routes.Count; second++)
            {
                if (string.Equals(routes[first].BundleKey, routes[second].BundleKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (SchematicSegment firstSegment in EnumerateSegments(routes[first].Points))
                {
                    foreach (SchematicSegment secondSegment in EnumerateSegments(routes[second].Points))
                    {
                        if (!TryFindOrthogonalCrossing(firstSegment, secondSegment, out Point crossing))
                        {
                            continue;
                        }

                        if (firstSegment.IsHorizontal)
                        {
                            bridges[first].Add(new SchematicRouteBridge(crossing, SchematicRouteBridgeOrientation.Horizontal));
                        }
                        else
                        {
                            bridges[second].Add(new SchematicRouteBridge(crossing, SchematicRouteBridgeOrientation.Horizontal));
                        }
                    }
                }
            }
        }

        return routes
            .Select((route, index) => route with { Bridges = bridges[index].Distinct().ToArray() })
            .ToArray();
    }

    private static IEnumerable<SchematicSegment> EnumerateSegments(IReadOnlyList<Point> points)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            if (Distance(points[index], points[index + 1]) > 0.01)
            {
                yield return new SchematicSegment(points[index], points[index + 1]);
            }
        }
    }

    private static bool TryFindOrthogonalCrossing(SchematicSegment first, SchematicSegment second, out Point crossing)
    {
        crossing = default;
        if (first.IsHorizontal == second.IsHorizontal)
        {
            return false;
        }

        SchematicSegment horizontal = first.IsHorizontal ? first : second;
        SchematicSegment vertical = first.IsHorizontal ? second : first;
        double x = vertical.Start.X;
        double y = horizontal.Start.Y;
        if (x <= Math.Min(horizontal.Start.X, horizontal.End.X) + 0.1
            || x >= Math.Max(horizontal.Start.X, horizontal.End.X) - 0.1
            || y <= Math.Min(vertical.Start.Y, vertical.End.Y) + 0.1
            || y >= Math.Max(vertical.Start.Y, vertical.End.Y) - 0.1)
        {
            return false;
        }

        crossing = new Point(x, y);
        return true;
    }

    private static IReadOnlyList<SchematicConnectionRoute> PlaceLabels(
        IReadOnlyList<SchematicConnectionRoute> routes,
        Rect bounds,
        bool compactLayout,
        IReadOnlyList<Rect> obstacles)
    {
        List<Rect> placed = [];
        List<SchematicConnectionRoute> result = [];
        foreach (SchematicConnectionRoute route in routes.OrderBy(static route => route.LabelBounds.Y).ThenBy(static route => route.LabelBounds.X))
        {
            Rect label = PlaceLabel(route.LabelBounds, bounds, compactLayout, placed, obstacles);
            placed.Add(label);
            result.Add(route with { LabelBounds = label });
        }

        return result;
    }

    private static Rect PlaceLabel(Rect preferredLabel, Rect bounds, bool compactLayout, IReadOnlyList<Rect> placedLabels, IReadOnlyList<Rect> obstacles)
    {
        double margin = compactLayout ? 10 : 14;
        double minX = bounds.X + margin;
        double maxX = Math.Max(minX, bounds.Right - margin);
        double minY = bounds.Y + margin;
        double maxY = Math.Max(minY, bounds.Bottom - margin);
        foreach (Rect candidate in EnumerateLabelCandidates(preferredLabel, compactLayout ? 20 : 24, compactLayout ? 32 : 42)
            .Select(candidate => ClampLabel(candidate, minX, maxX, minY, maxY)))
        {
            if (!placedLabels.Any(label => label.Inflate(2).Intersects(candidate))
                && !obstacles.Any(obstacle => obstacle.Inflate(3).Intersects(candidate)))
            {
                return candidate;
            }
        }

        return ClampLabel(preferredLabel, minX, maxX, minY, maxY);
    }

    private static IEnumerable<Rect> EnumerateLabelCandidates(Rect preferredLabel, double verticalStep, double horizontalStep)
    {
        yield return preferredLabel;
        for (int ring = 1; ring <= 12; ring++)
        {
            yield return new Rect(preferredLabel.X, preferredLabel.Y + verticalStep * ring, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X, preferredLabel.Y - verticalStep * ring, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X + horizontalStep * ring, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X - horizontalStep * ring, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
        }
    }

    private static Rect ClampLabel(Rect label, double minX, double maxX, double minY, double maxY)
    {
        return new Rect(
            Math.Clamp(label.X, minX, Math.Max(minX, maxX - label.Width)),
            Math.Clamp(label.Y, minY, Math.Max(minY, maxY - label.Height)),
            label.Width,
            label.Height);
    }

    private static Rect BuildLabelBounds(IReadOnlyList<Point> points, int width)
    {
        Point anchor = GetLabelAnchor(points);
        double labelWidth = width <= 1 ? 30 : Math.Clamp(24 + width * 2.4, 36, 72);
        return new Rect(anchor.X - labelWidth / 2, anchor.Y - 9, labelWidth, 18);
    }

    private static Point GetLabelAnchor(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return default;
        }

        if (points.Count == 1)
        {
            return points[0];
        }

        int segmentIndex = Math.Max(0, (points.Count - 1) / 2);
        Point start = points[segmentIndex];
        Point end = points[segmentIndex + 1];
        return new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
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
}

public sealed record SchematicGraph(IReadOnlyList<SchematicNet> Nets, IReadOnlyList<Rect> Obstacles);

public sealed record SchematicNet(string Key, IReadOnlyList<SchematicConnectionRouteRequest> Requests)
{
    public int Fanout => Requests.Count;

    public string PrimaryRequestId => Requests[0].Id;

    public SchematicConnectionRouteKind PrimaryKind =>
        Requests.Any(static request => request.Kind == SchematicConnectionRouteKind.BoundaryToChildInput)
            ? SchematicConnectionRouteKind.BoundaryToChildInput
            : Requests.Any(static request => request.Kind == SchematicConnectionRouteKind.ChildOutputToBoundary)
                ? SchematicConnectionRouteKind.ChildOutputToBoundary
                : Requests[0].Kind;

    public double AverageSource => Requests.Average(static request => request.Source.Y);

    public double AverageTarget => Requests.Average(static request => request.Target.Y);
}

internal sealed record RoutedLaneSet(
    IReadOnlyDictionary<string, double> InputLanes,
    IReadOnlyDictionary<string, double> OutputLanes,
    IReadOnlyDictionary<string, double> LocalLanes)
{
    public double GetLane(SchematicNet net)
    {
        return net.PrimaryKind switch
        {
            SchematicConnectionRouteKind.BoundaryToChildInput when InputLanes.TryGetValue(net.Key, out double lane) => lane,
            SchematicConnectionRouteKind.ChildOutputToBoundary when OutputLanes.TryGetValue(net.Key, out double lane) => lane,
            _ when LocalLanes.TryGetValue(net.Key, out double lane) => lane,
            _ => net.Requests.Average(static request => (request.Source.Y + request.Target.Y) / 2)
        };
    }
}

internal readonly record struct SchematicSegment(Point Start, Point End)
{
    public bool IsHorizontal => Math.Abs(Start.Y - End.Y) < 0.01;
}

internal readonly record struct PointKey(long X, long Y)
{
    public static PointKey From(Point point) => new((long)Math.Round(point.X * 10), (long)Math.Round(point.Y * 10));
}
