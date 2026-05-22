using Avalonia;

namespace Bistable.App.Services.Routing;

public sealed class SchematicMazeRouter : ISchematicRouter
{
    private const double GridBufferCompact = 14;
    private const double GridBufferNormal = 18;
    private const double GridFallbackStep = 18;
    private const double LabelMinWidth = 36;
    private const double LabelMaxWidth = 72;

    // Approach-column spreading: each port on the same module side gets a unique
    // offset column so routes are guaranteed to be visually separated.
    private const double ApproachBaseCompact = 10.0;
    private const double ApproachBaseNormal = 12.0;
    private const double ApproachSpacingCompact = 7.0;
    private const double ApproachSpacingNormal = 9.0;
    private const double PortEdgeTolerance = 1.5;

    public IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input)
    {
        if (input.Requests.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Rect> obstacles = input.Obstacles ?? [];
        SchematicNet[] nets = input.Requests
            .GroupBy(static request => request.BundleKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new SchematicNet(group.Key, group.OrderBy(static request => request.Id, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderByDescending(static net => net.Fanout)
            .ThenBy(static net => SourceTargetSpan(net))
            .ThenBy(static net => net.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Dictionary<string, (Point Src, Point Tgt)> approaches =
            ComputeApproachPoints(input.Requests, obstacles, input.CompactLayout);

        HananGrid grid = BuildGrid(input.Layout.PanelRect, obstacles, input.Requests, input.CompactLayout, approaches);
        MazeRouter router = new();
        CongestionMap congestion = new();

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicNet net in nets)
        {
            if (net.Fanout > 1 && AllShareSource(net.Requests))
            {
                RouteSteinerFanout(net, grid, router, obstacles, congestion, routes, approaches);
            }
            else
            {
                foreach (SchematicConnectionRouteRequest request in net.Requests)
                {
                    approaches.TryGetValue(request.Id, out (Point Src, Point Tgt) ap);
                    Point routeSrc = ap.Src == default ? request.Source : ap.Src;
                    Point routeTgt = ap.Tgt == default ? request.Target : ap.Tgt;

                    IReadOnlyList<Point>? corePath = router.FindPath(grid, routeSrc, routeTgt, obstacles, congestion);
                    if (corePath is null || corePath.Count < 2)
                    {
                        corePath = BuildFallbackTwoPoint(routeSrc, routeTgt);
                    }

                    IReadOnlyList<Point> path = BuildPathWithStubs(request.Source, routeSrc, corePath, routeTgt, request.Target);
                    MarkCongestion(grid, path, congestion);
                    routes.Add(new SchematicConnectionRoute(
                        request.Id,
                        net.Key,
                        net.Fanout,
                        string.Equals(net.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                        path,
                        BuildLabelBounds(path, request.LabelWidth),
                        GetLabelAnchor(path)));
                }
            }
        }

        IReadOnlyList<SchematicConnectionRoute> assigned = TrackAssigner.Assign(routes, input.CompactLayout);
        IReadOnlyList<SchematicConnectionRoute> withJunctions = DetectJunctions(assigned);
        IReadOnlyList<SchematicConnectionRoute> withBridges = DetectBridges(withJunctions);
        return PlaceLabels(withBridges, input.Layout.PanelRect, input.CompactLayout, obstacles);
    }

    // For each obstacle side that has 2+ ports, assign each port a unique approach
    // column so that routes are spread apart before entering/leaving the module.
    private static Dictionary<string, (Point Src, Point Tgt)> ComputeApproachPoints(
        IReadOnlyList<SchematicConnectionRouteRequest> requests,
        IReadOnlyList<Rect> obstacles,
        bool compact)
    {
        double baseMargin = compact ? ApproachBaseCompact : ApproachBaseNormal;
        double spacing = compact ? ApproachSpacingCompact : ApproachSpacingNormal;

        // Key: obstacleIndex * 4 + side(0=left,1=right,2=top,3=bottom)
        // Value: list of (requestId, coord along side, isSource)
        Dictionary<long, List<(string Id, double Coord, bool IsSource)>> sideGroups = [];

        void Collect(int obsIdx, int side, string id, double coord, bool isSource)
        {
            long key = (long)obsIdx * 4 + side;
            if (!sideGroups.TryGetValue(key, out List<(string, double, bool)>? list))
            {
                list = [];
                sideGroups[key] = list;
            }

            list.Add((id, coord, isSource));
        }

        for (int i = 0; i < requests.Count; i++)
        {
            SchematicConnectionRouteRequest req = requests[i];
            for (int oi = 0; oi < obstacles.Count; oi++)
            {
                Rect obs = obstacles[oi];
                // Source on left or right vertical edge
                if (Math.Abs(req.Source.Y - (obs.Y + obs.Bottom) / 2) <= obs.Height / 2 + PortEdgeTolerance)
                {
                    if (Math.Abs(req.Source.X - obs.X) < PortEdgeTolerance)
                    {
                        Collect(oi, 0, req.Id, req.Source.Y, isSource: true);
                        break;
                    }

                    if (Math.Abs(req.Source.X - obs.Right) < PortEdgeTolerance)
                    {
                        Collect(oi, 1, req.Id, req.Source.Y, isSource: true);
                        break;
                    }
                }
            }

            for (int oi = 0; oi < obstacles.Count; oi++)
            {
                Rect obs = obstacles[oi];
                // Target on left or right vertical edge
                if (Math.Abs(req.Target.Y - (obs.Y + obs.Bottom) / 2) <= obs.Height / 2 + PortEdgeTolerance)
                {
                    if (Math.Abs(req.Target.X - obs.X) < PortEdgeTolerance)
                    {
                        Collect(oi, 0, req.Id, req.Target.Y, isSource: false);
                        break;
                    }

                    if (Math.Abs(req.Target.X - obs.Right) < PortEdgeTolerance)
                    {
                        Collect(oi, 1, req.Id, req.Target.Y, isSource: false);
                        break;
                    }
                }
            }
        }

        Dictionary<string, Point> srcApproach = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Point> tgtApproach = new(StringComparer.OrdinalIgnoreCase);

        foreach ((long key, List<(string Id, double Coord, bool IsSource)> group) in sideGroups)
        {
            if (group.Count <= 1)
            {
                continue;
            }

            int obsIdx = (int)(key / 4);
            int side = (int)(key % 4);
            Rect obs = obstacles[obsIdx];
            List<(string Id, double Coord, bool IsSource)> sorted = [.. group.OrderBy(static p => p.Coord)];

            for (int rank = 0; rank < sorted.Count; rank++)
            {
                (string id, double coord, bool isSource) = sorted[rank];
                double approachX = side == 0
                    ? obs.X - baseMargin - rank * spacing
                    : obs.Right + baseMargin + rank * spacing;
                Point approachPt = new(approachX, coord);
                if (isSource)
                {
                    srcApproach[id] = approachPt;
                }
                else
                {
                    tgtApproach[id] = approachPt;
                }
            }
        }

        Dictionary<string, (Point Src, Point Tgt)> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (SchematicConnectionRouteRequest req in requests)
        {
            bool hasSrc = srcApproach.TryGetValue(req.Id, out Point sa);
            bool hasTgt = tgtApproach.TryGetValue(req.Id, out Point ta);
            if (hasSrc || hasTgt)
            {
                result[req.Id] = (hasSrc ? sa : req.Source, hasTgt ? ta : req.Target);
            }
        }

        return result;
    }

    // Combine an optional source stub, the A*-routed core path, and an optional target stub.
    private static IReadOnlyList<Point> BuildPathWithStubs(
        Point actualSrc,
        Point routeSrc,
        IReadOnlyList<Point> corePath,
        Point routeTgt,
        Point actualTgt)
    {
        bool hasSrcStub = Math.Abs(actualSrc.X - routeSrc.X) > 0.1 || Math.Abs(actualSrc.Y - routeSrc.Y) > 0.1;
        bool hasTgtStub = Math.Abs(routeTgt.X - actualTgt.X) > 0.1 || Math.Abs(routeTgt.Y - actualTgt.Y) > 0.1;
        if (!hasSrcStub && !hasTgtStub)
        {
            return corePath;
        }

        List<Point> result = new(corePath.Count + 2);
        if (hasSrcStub)
        {
            result.Add(actualSrc);
        }

        result.AddRange(corePath);
        if (hasTgtStub)
        {
            result.Add(actualTgt);
        }

        return CompactPath(result);
    }

    private static IReadOnlyList<Point> CompactPath(List<Point> points)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        List<Point> result = [points[0]];
        for (int i = 1; i < points.Count - 1; i++)
        {
            Point prev = result[^1];
            Point cur = points[i];
            Point next = points[i + 1];
            bool hCollinear = Math.Abs(prev.Y - cur.Y) < 0.01 && Math.Abs(cur.Y - next.Y) < 0.01;
            bool vCollinear = Math.Abs(prev.X - cur.X) < 0.01 && Math.Abs(cur.X - next.X) < 0.01;
            if (!hCollinear && !vCollinear)
            {
                result.Add(cur);
            }
        }

        result.Add(points[^1]);
        return result;
    }

    private static HananGrid BuildGrid(
        Rect panelBounds,
        IReadOnlyList<Rect> obstacles,
        IReadOnlyList<SchematicConnectionRouteRequest> requests,
        bool compactLayout,
        Dictionary<string, (Point Src, Point Tgt)>? approaches = null)
    {
        List<double> xCoords = [];
        List<double> yCoords = [];
        double buffer = compactLayout ? GridBufferCompact : GridBufferNormal;
        foreach (Rect obstacle in obstacles)
        {
            xCoords.Add(obstacle.X - buffer);
            xCoords.Add(obstacle.Right + buffer);
            yCoords.Add(obstacle.Y - buffer);
            yCoords.Add(obstacle.Bottom + buffer);
        }

        foreach (SchematicConnectionRouteRequest request in requests)
        {
            xCoords.Add(request.Source.X);
            xCoords.Add(request.Target.X);
            yCoords.Add(request.Source.Y);
            yCoords.Add(request.Target.Y);
        }

        if (approaches is not null)
        {
            foreach ((Point src, Point tgt) in approaches.Values)
            {
                xCoords.Add(src.X);
                yCoords.Add(src.Y);
                xCoords.Add(tgt.X);
                yCoords.Add(tgt.Y);
            }
        }

        if (panelBounds.Width > 0)
        {
            for (double x = panelBounds.X + GridFallbackStep; x < panelBounds.Right; x += GridFallbackStep)
            {
                if (!IsInsideObstacleBufferX(x, obstacles, buffer))
                {
                    xCoords.Add(x);
                }
            }

            for (double y = panelBounds.Y + GridFallbackStep; y < panelBounds.Bottom; y += GridFallbackStep)
            {
                if (!IsInsideObstacleBufferY(y, obstacles, buffer))
                {
                    yCoords.Add(y);
                }
            }
        }

        return new HananGrid(panelBounds, xCoords, yCoords);
    }

    private static bool IsInsideObstacleBufferX(double x, IReadOnlyList<Rect> obstacles, double buffer)
    {
        foreach (Rect obstacle in obstacles)
        {
            if ((x > obstacle.X - buffer && x < obstacle.X)
                || (x > obstacle.Right && x < obstacle.Right + buffer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideObstacleBufferY(double y, IReadOnlyList<Rect> obstacles, double buffer)
    {
        foreach (Rect obstacle in obstacles)
        {
            if ((y > obstacle.Y - buffer && y < obstacle.Y)
                || (y > obstacle.Bottom && y < obstacle.Bottom + buffer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AllShareSource(IReadOnlyList<SchematicConnectionRouteRequest> requests)
    {
        Point first = requests[0].Source;
        for (int i = 1; i < requests.Count; i++)
        {
            if (Math.Abs(requests[i].Source.X - first.X) > 1 || Math.Abs(requests[i].Source.Y - first.Y) > 1)
            {
                return false;
            }
        }

        return true;
    }

    private static void RouteSteinerFanout(
        SchematicNet net,
        HananGrid grid,
        MazeRouter router,
        IReadOnlyList<Rect> obstacles,
        CongestionMap congestion,
        List<SchematicConnectionRoute> routes,
        Dictionary<string, (Point Src, Point Tgt)> approaches)
    {
        Point source = net.Requests[0].Source;
        Point[] allPoints = new Point[net.Requests.Count + 1];
        allPoints[0] = source;
        for (int i = 0; i < net.Requests.Count; i++)
        {
            SchematicConnectionRouteRequest req = net.Requests[i];
            allPoints[i + 1] = approaches.TryGetValue(req.Id, out (Point Src, Point Tgt) ap) && ap.Tgt != default
                ? ap.Tgt
                : req.Target;
        }

        (int[] parent, IReadOnlyList<(int From, int To)> mstEdges) = RectilinearSteinerTree.Build(allPoints);
        IReadOnlyList<(int From, int To)> orderedEdges = RectilinearSteinerTree.BfsOrder(mstEdges, allPoints.Length);

        Dictionary<(int From, int To), IReadOnlyList<Point>> edgePaths = new(orderedEdges.Count);
        foreach ((int from, int to) in orderedEdges)
        {
            IReadOnlyList<Point>? path = router.FindPath(grid, allPoints[from], allPoints[to], obstacles, congestion);
            if (path is null || path.Count < 2)
            {
                path = BuildFallbackTwoPoint(allPoints[from], allPoints[to]);
            }

            MarkCongestion(grid, path, congestion);
            edgePaths[(from, to)] = path;
        }

        for (int requestIdx = 0; requestIdx < net.Requests.Count; requestIdx++)
        {
            SchematicConnectionRouteRequest request = net.Requests[requestIdx];
            int targetPointIdx = requestIdx + 1;
            IReadOnlyList<int> nodeSequence = RectilinearSteinerTree.PathToTarget(targetPointIdx, parent);

            List<Point> fullPath = [];
            for (int step = 0; step < nodeSequence.Count - 1; step++)
            {
                int from = nodeSequence[step];
                int to = nodeSequence[step + 1];
                IReadOnlyList<Point> segment = edgePaths[(from, to)];

                if (fullPath.Count == 0)
                {
                    fullPath.AddRange(segment);
                }
                else
                {
                    for (int p = 1; p < segment.Count; p++)
                    {
                        fullPath.Add(segment[p]);
                    }
                }
            }

            if (fullPath.Count < 2)
            {
                fullPath = [source, allPoints[targetPointIdx]];
            }

            // Append stub from approach point to actual port if approach was used
            Point routedTarget = allPoints[targetPointIdx];
            Point actualTarget = request.Target;
            if (Math.Abs(routedTarget.X - actualTarget.X) > 0.1 || Math.Abs(routedTarget.Y - actualTarget.Y) > 0.1)
            {
                fullPath.Add(actualTarget);
            }

            IReadOnlyList<Point> finalPath = CompactPath(fullPath);
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                net.Key,
                net.Fanout,
                string.Equals(net.PrimaryRequestId, request.Id, StringComparison.OrdinalIgnoreCase),
                finalPath,
                BuildLabelBounds(finalPath, request.LabelWidth),
                GetLabelAnchor(finalPath)));
        }
    }

    private static IReadOnlyList<Point> BuildFallbackTwoPoint(Point source, Point target)
    {
        if (Math.Abs(source.X - target.X) < 0.5 || Math.Abs(source.Y - target.Y) < 0.5)
        {
            return [source, target];
        }

        return [source, new Point(target.X, source.Y), target];
    }

    private static void MarkCongestion(HananGrid grid, IReadOnlyList<Point> path, CongestionMap congestion)
    {
        for (int index = 0; index < path.Count - 1; index++)
        {
            (int col1, int row1) = grid.NearestCell(path[index]);
            (int col2, int row2) = grid.NearestCell(path[index + 1]);
            if (col1 == col2)
            {
                int from = Math.Min(row1, row2);
                int to = Math.Max(row1, row2);
                for (int row = from; row <= to; row++)
                {
                    congestion.Increment(col1, row);
                }

                continue;
            }

            if (row1 == row2)
            {
                int from = Math.Min(col1, col2);
                int to = Math.Max(col1, col2);
                for (int col = from; col <= to; col++)
                {
                    congestion.Increment(col, row1);
                }
            }
        }
    }

    private static double SourceTargetSpan(SchematicNet net) =>
        net.Requests.Sum(static request =>
            Math.Abs(request.Source.X - request.Target.X) + Math.Abs(request.Source.Y - request.Target.Y));

    private static Rect BuildLabelBounds(IReadOnlyList<Point> points, int width)
    {
        Point anchor = GetLabelAnchor(points);
        double labelWidth = width <= 1 ? LabelMinWidth - 6 : Math.Clamp(24 + width * 2.4, LabelMinWidth, LabelMaxWidth);
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

    private static IReadOnlyList<SchematicConnectionRoute> DetectJunctions(IReadOnlyList<SchematicConnectionRoute> routes)
    {
        Dictionary<string, HashSet<PointKey>> junctionsByNet = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SchematicConnectionRoute> netRoutes in routes.GroupBy(static route => route.BundleKey, StringComparer.OrdinalIgnoreCase))
        {
            Dictionary<PointKey, int> counts = [];
            SchematicConnectionRoute[] grouped = [.. netRoutes];
            foreach (SchematicConnectionRoute route in grouped)
            {
                if (route.Points.Count <= 2)
                {
                    continue;
                }

                for (int index = 1; index < route.Points.Count - 1; index++)
                {
                    PointKey key = PointKey.From(route.Points[index]);
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }

            HashSet<PointKey> junctions = counts
                .Where(static item => item.Value >= 2)
                .Select(static item => item.Key)
                .ToHashSet();

            if (grouped.Length > 1)
            {
                AddSharedEndpointJunctions(grouped, junctions);
            }

            junctionsByNet[netRoutes.Key] = junctions;
        }

        return routes
            .Select(route => route with
            {
                Junctions = ExtractJunctions(route, junctionsByNet)
            })
            .ToArray();
    }

    private static void AddSharedEndpointJunctions(IReadOnlyList<SchematicConnectionRoute> netRoutes, HashSet<PointKey> junctions)
    {
        Dictionary<PointKey, int> sourceCounts = [];
        Dictionary<PointKey, int> targetCounts = [];
        foreach (SchematicConnectionRoute route in netRoutes)
        {
            if (route.Points.Count == 0)
            {
                continue;
            }

            PointKey sourceKey = PointKey.From(route.Points[0]);
            sourceCounts[sourceKey] = sourceCounts.GetValueOrDefault(sourceKey) + 1;
            PointKey targetKey = PointKey.From(route.Points[^1]);
            targetCounts[targetKey] = targetCounts.GetValueOrDefault(targetKey) + 1;
        }

        foreach ((PointKey key, int count) in sourceCounts)
        {
            if (count >= 2)
            {
                junctions.Add(key);
            }
        }

        foreach ((PointKey key, int count) in targetCounts)
        {
            if (count >= 2)
            {
                junctions.Add(key);
            }
        }
    }

    private static IReadOnlyList<Point> ExtractJunctions(SchematicConnectionRoute route, IReadOnlyDictionary<string, HashSet<PointKey>> junctionsByNet)
    {
        if (!junctionsByNet.TryGetValue(route.BundleKey, out HashSet<PointKey>? keys) || keys.Count == 0)
        {
            return [];
        }

        HashSet<PointKey> seen = [];
        List<Point> result = [];
        for (int index = 0; index < route.Points.Count; index++)
        {
            Point point = route.Points[index];
            PointKey key = PointKey.From(point);
            if (keys.Contains(key) && seen.Add(key))
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static IReadOnlyList<SchematicConnectionRoute> DetectBridges(IReadOnlyList<SchematicConnectionRoute> routes)
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

                        SchematicRouteBridge bridge = new(crossing, SchematicRouteBridgeOrientation.Horizontal);
                        if (firstSegment.IsHorizontal)
                        {
                            bridges[first].Add(bridge);
                        }
                        else
                        {
                            bridges[second].Add(bridge);
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
            Point start = points[index];
            Point end = points[index + 1];
            if (Math.Abs(start.X - end.X) > 0.01 || Math.Abs(start.Y - end.Y) > 0.01)
            {
                yield return new SchematicSegment(start, end);
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
        if (routes.Count == 0)
        {
            return routes;
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
            .Select(route => route with
            {
                LabelBounds = labels.TryGetValue(route.Id, out Rect label) ? label : route.LabelBounds
            })
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
        for (int ring = 1; ring <= 14; ring++)
        {
            yield return new Rect(preferredLabel.X, preferredLabel.Y + verticalStep * ring, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X, preferredLabel.Y - verticalStep * ring, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X + horizontalStep * ring, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
            yield return new Rect(preferredLabel.X - horizontalStep * ring, preferredLabel.Y, preferredLabel.Width, preferredLabel.Height);
        }
    }

    private static Rect ClampLabel(Rect label, double minX, double maxX, double minY, double maxY) =>
        new(
            Math.Clamp(label.X, minX, Math.Max(minX, maxX - label.Width)),
            Math.Clamp(label.Y, minY, Math.Max(minY, maxY - label.Height)),
            label.Width,
            label.Height);

    private readonly record struct SchematicSegment(Point Start, Point End)
    {
        public bool IsHorizontal => Math.Abs(Start.Y - End.Y) < 0.01;
    }

    private readonly record struct PointKey(long X, long Y)
    {
        public static PointKey From(Point point) => new((long)Math.Round(point.X * 10), (long)Math.Round(point.Y * 10));
    }
}
