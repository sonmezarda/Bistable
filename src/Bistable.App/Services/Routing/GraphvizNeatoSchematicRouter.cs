using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;

namespace Bistable.App.Services.Routing;

public sealed class GraphvizNeatoSchematicRouter : ISchematicRouter
{
    private const double GraphvizScale = 72.0;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    public GraphvizNeatoSchematicRouter()
        : this("neato", TimeSpan.FromSeconds(4))
    {
    }

    public GraphvizNeatoSchematicRouter(string executablePath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Graphviz executable path must not be empty.", nameof(executablePath));
        }

        _executablePath = executablePath;
        _timeout = timeout;
    }

    public IReadOnlyList<SchematicConnectionRoute> Compute(SchematicConnectionRoutingInput input)
    {
        if (input.Requests.Count == 0)
        {
            return [];
        }

        SchematicConnectionRoutingInput normalizedInput = NormalizeInput(input);
        GraphvizCoordinateSpace coordinates = GraphvizCoordinateSpace.From(normalizedInput);
        string dot = BuildDot(normalizedInput, coordinates);
        string plain = RunNeato(dot);
        IReadOnlyDictionary<string, GraphvizPlainEdge> edges = GraphvizPlainParser.ParseEdges(plain);
        IReadOnlyList<SchematicConnectionRoute> routes = BuildRoutes(normalizedInput, coordinates, edges);
        ValidateRoutes(normalizedInput, routes);
        return routes;
    }

    private static SchematicConnectionRoutingInput NormalizeInput(SchematicConnectionRoutingInput input)
    {
        if (input.Obstacles is null || input.Obstacles.Count == 0)
        {
            return input;
        }

        Rect panel = input.Layout.PanelRect;
        Rect current = input.Layout.CurrentNodeRect;
        IReadOnlyList<Rect> obstacles = input.Obstacles
            .Where(obstacle => !IsScopeContainerObstacle(obstacle, panel, current))
            .ToArray();

        return input with { Obstacles = obstacles };
    }

    private static bool IsScopeContainerObstacle(Rect obstacle, Rect panel, Rect current)
    {
        if (SameRect(obstacle, panel) || SameRect(obstacle, current) && current.Width > panel.Width * 0.72)
        {
            return true;
        }

        return obstacle.Width > panel.Width * 0.72 && obstacle.Height > panel.Height * 0.62;
    }

    private static bool SameRect(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 0.5
        && Math.Abs(first.Y - second.Y) < 0.5
        && Math.Abs(first.Width - second.Width) < 0.5
        && Math.Abs(first.Height - second.Height) < 0.5;

    private string RunNeato(string dot)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            Arguments = "-n2 -Tplain",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new SchematicRoutingException("Graphviz neato could not be started.");
            process.StandardInput.Write(dot);
            process.StandardInput.Close();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(_timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw new SchematicRoutingException($"Graphviz neato routing timed out after {_timeout.TotalSeconds:0.#} seconds.");
            }

            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new SchematicRoutingException(
                    string.IsNullOrWhiteSpace(error)
                        ? $"Graphviz neato exited with code {process.ExitCode}."
                        : $"Graphviz neato exited with code {process.ExitCode}: {error.Trim()}");
            }

            return output;
        }
        catch (Win32Exception exception)
        {
            throw new SchematicRoutingException(
                $"Graphviz neato executable was not found. Install Graphviz or switch the schematic router back to Internal. Executable: '{_executablePath}'.",
                exception);
        }
    }

    private static string BuildDot(SchematicConnectionRoutingInput input, GraphvizCoordinateSpace coordinates)
    {
        StringBuilder builder = new();
        builder.AppendLine("graph G {");
        builder.AppendLine("  graph [layout=neato, splines=ortho, overlap=false, outputorder=edgesfirst, margin=0, sep=\"+16\"];");
        builder.AppendLine("  node [label=\"\", fixedsize=true, pin=true, fontsize=1];");
        builder.AppendLine("  edge [decorate=false, dir=none, penwidth=1];");

        for (int index = 0; index < input.Requests.Count; index++)
        {
            SchematicConnectionRouteRequest request = input.Requests[index];
            AppendPointNode(builder, SourceNode(index), coordinates.ToGraphviz(request.Source));
            AppendPointNode(builder, TargetNode(index), coordinates.ToGraphviz(request.Target));
            builder
                .Append("  ")
                .Append(SourceNode(index))
                .Append(" -- ")
                .Append(TargetNode(index))
                .AppendLine(";");
        }

        if (input.Obstacles is not null)
        {
            for (int index = 0; index < input.Obstacles.Count; index++)
            {
                Rect obstacle = input.Obstacles[index].Inflate(8);
                Point center = coordinates.ToGraphviz(obstacle.Center);
                builder
                    .Append("  obstacle")
                    .Append(index.ToString(Invariant))
                    .Append(" [shape=box, style=filled, color=\"#2a3241\", fillcolor=\"#2a3241\", width=\"")
                    .Append((Math.Max(obstacle.Width, 1) / GraphvizScale).ToString("0.###", Invariant))
                    .Append("\", height=\"")
                    .Append((Math.Max(obstacle.Height, 1) / GraphvizScale).ToString("0.###", Invariant))
                    .Append("\", pos=\"")
                    .Append(center.X.ToString("0.###", Invariant))
                    .Append(',')
                    .Append(center.Y.ToString("0.###", Invariant))
                    .AppendLine("!\"];");
            }
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendPointNode(StringBuilder builder, string id, Point graphvizPoint)
    {
        builder
            .Append("  ")
            .Append(id)
            .Append(" [shape=point, width=\"0.035\", height=\"0.035\", pos=\"")
            .Append(graphvizPoint.X.ToString("0.###", Invariant))
            .Append(',')
            .Append(graphvizPoint.Y.ToString("0.###", Invariant))
            .AppendLine("!\"];");
    }

    private static IReadOnlyList<SchematicConnectionRoute> BuildRoutes(
        SchematicConnectionRoutingInput input,
        GraphvizCoordinateSpace coordinates,
        IReadOnlyDictionary<string, GraphvizPlainEdge> edges)
    {
        Dictionary<string, int> bundleSizes = input.Requests
            .GroupBy(static request => request.BundleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        HashSet<string> primaryBundles = new(StringComparer.OrdinalIgnoreCase);
        List<SchematicConnectionRoute> routes = new(input.Requests.Count);

        for (int index = 0; index < input.Requests.Count; index++)
        {
            SchematicConnectionRouteRequest request = input.Requests[index];
            string edgeKey = GraphvizPlainEdge.BuildKey(SourceNode(index), TargetNode(index));
            if (!edges.TryGetValue(edgeKey, out GraphvizPlainEdge? edge))
            {
                throw new SchematicRoutingException($"Graphviz neato did not return a route for '{request.Id}'.");
            }

            List<Point> points = edge.Points
                .Select(coordinates.ToAvalonia)
                .ToList();
            points.Insert(0, request.Source);
            points.Add(request.Target);
            points = Simplify(Orthogonalize(points, request.Kind));
            points = DetourAroundObstacles(points, input.Obstacles ?? [], request);

            bool primary = primaryBundles.Add(request.BundleKey);
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                request.BundleKey,
                bundleSizes[request.BundleKey],
                primary,
                points,
                PlaceLabel(input.Layout.PanelRect, request, points, routes.Count),
                points.Count > 1 ? points[1] : request.Source));
        }

        return AddJunctionAndBridgeMetadata(routes);
    }

    private static void ValidateRoutes(SchematicConnectionRoutingInput input, IReadOnlyList<SchematicConnectionRoute> routes)
    {
        IReadOnlyList<Rect> obstacles = input.Obstacles ?? [];
        if (obstacles.Count == 0)
        {
            return;
        }

        Dictionary<string, SchematicConnectionRouteRequest> requests = input.Requests.ToDictionary(static request => request.Id, StringComparer.OrdinalIgnoreCase);
        foreach (SchematicConnectionRoute route in routes)
        {
            SchematicConnectionRouteRequest request = requests[route.Id];
            foreach (Rect obstacle in obstacles)
            {
                Rect interior = obstacle.Deflate(4);
                if (interior.Width <= 0 || interior.Height <= 0)
                {
                    continue;
                }

                if (PointTouchesObstacle(request.Source, obstacle) || PointTouchesObstacle(request.Target, obstacle))
                {
                    continue;
                }

                for (int index = 0; index < route.Points.Count - 1; index++)
                {
                    if (SegmentIntersectsInterior(route.Points[index], route.Points[index + 1], interior))
                    {
                        throw new SchematicRoutingException(
                            $"Graphviz produced an invalid route through an obstacle for '{request.SignalName}'. This backend needs a full-layout adapter before it can be used as the default router.");
                    }
                }
            }
        }
    }

    private static List<Point> DetourAroundObstacles(
        IReadOnlyList<Point> points,
        IReadOnlyList<Rect> obstacles,
        SchematicConnectionRouteRequest request)
    {
        if (points.Count < 2 || obstacles.Count == 0)
        {
            return [.. points];
        }

        List<Point> routed = [.. points];
        for (int pass = 0; pass < 6; pass++)
        {
            bool changed = false;
            List<Point> next = [routed[0]];
            for (int index = 0; index < routed.Count - 1; index++)
            {
                Point start = next[^1];
                Point end = routed[index + 1];
                IReadOnlyList<Point> segment = DetourSegment(start, end, obstacles, request);
                changed |= segment.Count > 2;
                for (int pointIndex = 1; pointIndex < segment.Count; pointIndex++)
                {
                    if (!SamePoint(next[^1], segment[pointIndex]))
                    {
                        next.Add(segment[pointIndex]);
                    }
                }
            }

            routed = Simplify(next);
            if (!changed)
            {
                break;
            }
        }

        return routed;
    }

    private static IReadOnlyList<Point> DetourSegment(
        Point start,
        Point end,
        IReadOnlyList<Rect> obstacles,
        SchematicConnectionRouteRequest request)
    {
        Rect? blocker = FindBlockingObstacle(start, end, obstacles);
        if (blocker is null)
        {
            return [start, end];
        }

        Rect obstacle = blocker.Value.Inflate(12);
        if (Math.Abs(start.Y - end.Y) < 0.5)
        {
            double detourY = ChooseHorizontalDetourY(start, end, obstacle, request);
            return [start, new Point(start.X, detourY), new Point(end.X, detourY), end];
        }

        if (Math.Abs(start.X - end.X) < 0.5)
        {
            double detourX = ChooseVerticalDetourX(start, end, obstacle, request);
            return [start, new Point(detourX, start.Y), new Point(detourX, end.Y), end];
        }

        return [start, end];
    }

    private static Rect? FindBlockingObstacle(Point start, Point end, IReadOnlyList<Rect> obstacles)
    {
        foreach (Rect obstacle in obstacles)
        {
            Rect interior = obstacle.Deflate(3);
            if (interior.Width <= 0 || interior.Height <= 0)
            {
                continue;
            }

            if (PointTouchesObstacle(start, obstacle) || PointTouchesObstacle(end, obstacle))
            {
                continue;
            }

            if (SegmentIntersectsInterior(start, end, interior))
            {
                return obstacle;
            }
        }

        return null;
    }

    private static double ChooseHorizontalDetourY(Point start, Point end, Rect obstacle, SchematicConnectionRouteRequest request)
    {
        double above = obstacle.Y - 14;
        double below = obstacle.Bottom + 14;
        double sourceBias = (start.Y + end.Y) / 2;
        bool preferAbove = request.RoutesToChildInput
            ? sourceBias < obstacle.Center.Y
            : sourceBias <= obstacle.Center.Y;
        return preferAbove ? above : below;
    }

    private static double ChooseVerticalDetourX(Point start, Point end, Rect obstacle, SchematicConnectionRouteRequest request)
    {
        double left = obstacle.X - 14;
        double right = obstacle.Right + 14;
        double sourceBias = (start.X + end.X) / 2;
        bool preferRight = request.RoutesFromChildOutput || sourceBias >= obstacle.Center.X;
        return preferRight ? right : left;
    }

    private static bool SegmentIntersectsInterior(Point start, Point end, Rect rect)
    {
        if (Math.Abs(start.Y - end.Y) < 0.5)
        {
            double minX = Math.Min(start.X, end.X);
            double maxX = Math.Max(start.X, end.X);
            return start.Y > rect.Y && start.Y < rect.Bottom && maxX > rect.X && minX < rect.Right;
        }

        if (Math.Abs(start.X - end.X) < 0.5)
        {
            double minY = Math.Min(start.Y, end.Y);
            double maxY = Math.Max(start.Y, end.Y);
            return start.X > rect.X && start.X < rect.Right && maxY > rect.Y && minY < rect.Bottom;
        }

        return false;
    }

    private static bool PointTouchesObstacle(Point point, Rect obstacle) =>
        point.X >= obstacle.X - 1
        && point.X <= obstacle.Right + 1
        && point.Y >= obstacle.Y - 1
        && point.Y <= obstacle.Bottom + 1
        && (Math.Abs(point.X - obstacle.X) < 1
            || Math.Abs(point.X - obstacle.Right) < 1
            || Math.Abs(point.Y - obstacle.Y) < 1
            || Math.Abs(point.Y - obstacle.Bottom) < 1);

    private static List<Point> Orthogonalize(IReadOnlyList<Point> points, SchematicConnectionRouteKind kind)
    {
        List<Point> result = new(points.Count * 2);
        if (points.Count == 0)
        {
            return result;
        }

        result.Add(points[0]);
        for (int index = 1; index < points.Count; index++)
        {
            Point previous = result[^1];
            Point next = points[index];
            if (SamePoint(previous, next))
            {
                continue;
            }

            bool horizontal = Math.Abs(previous.Y - next.Y) < 0.5;
            bool vertical = Math.Abs(previous.X - next.X) < 0.5;
            if (!horizontal && !vertical)
            {
                Point elbow = PreferHorizontalFirst(kind)
                    ? new Point(next.X, previous.Y)
                    : new Point(previous.X, next.Y);
                if (!SamePoint(previous, elbow))
                {
                    result.Add(elbow);
                }
            }

            result.Add(next);
        }

        return result;
    }

    private static bool PreferHorizontalFirst(SchematicConnectionRouteKind kind) =>
        kind is SchematicConnectionRouteKind.BoundaryToChildInput
            or SchematicConnectionRouteKind.ChildOutputToBoundary
            or SchematicConnectionRouteKind.ChildOutputToChildInput;

    private static List<Point> Simplify(IReadOnlyList<Point> points)
    {
        List<Point> compact = [];
        foreach (Point point in points)
        {
            if (compact.Count == 0 || !SamePoint(compact[^1], point))
            {
                compact.Add(Round(point));
            }
        }

        for (int index = 1; index < compact.Count - 1;)
        {
            Point previous = compact[index - 1];
            Point current = compact[index];
            Point next = compact[index + 1];
            bool sameHorizontal = Math.Abs(previous.Y - current.Y) < 0.5 && Math.Abs(current.Y - next.Y) < 0.5;
            bool sameVertical = Math.Abs(previous.X - current.X) < 0.5 && Math.Abs(current.X - next.X) < 0.5;
            if (sameHorizontal || sameVertical)
            {
                compact.RemoveAt(index);
                continue;
            }

            index++;
        }

        return compact;
    }

    private static Rect PlaceLabel(Rect panel, SchematicConnectionRouteRequest request, IReadOnlyList<Point> points, int routeIndex)
    {
        double width = Math.Clamp(request.LabelWidth <= 1 ? 58 : request.LabelWidth * 7 + 28, 46, 104);
        double height = 18;
        Point anchor = points.Count > 1 ? points[Math.Min(1, points.Count - 1)] : request.Source;
        double x = anchor.X + 8;
        double y = anchor.Y - height - 7 - routeIndex % 4 * 4;
        x = Math.Clamp(x, panel.X + 8, Math.Max(panel.X + 8, panel.Right - width - 8));
        y = Math.Clamp(y, panel.Y + 8, Math.Max(panel.Y + 8, panel.Bottom - height - 8));
        return new Rect(x, y, width, height);
    }

    private static IReadOnlyList<SchematicConnectionRoute> AddJunctionAndBridgeMetadata(IReadOnlyList<SchematicConnectionRoute> routes)
    {
        Dictionary<string, List<Point>> junctions = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SchematicConnectionRoute> group in routes.GroupBy(static route => route.BundleKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            Point source = group.First().Points[0];
            junctions[group.Key] = [source];
        }

        Dictionary<string, List<SchematicRouteBridge>> bridges = new(StringComparer.OrdinalIgnoreCase);
        for (int first = 0; first < routes.Count; first++)
        {
            for (int second = first + 1; second < routes.Count; second++)
            {
                if (string.Equals(routes[first].BundleKey, routes[second].BundleKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddBridges(routes[first], routes[second], bridges);
            }
        }

        return routes.Select(route => route with
        {
            Junctions = junctions.TryGetValue(route.BundleKey, out List<Point>? routeJunctions) ? routeJunctions : [],
            Bridges = bridges.TryGetValue(route.Id, out List<SchematicRouteBridge>? routeBridges) ? routeBridges : []
        }).ToArray();
    }

    private static void AddBridges(
        SchematicConnectionRoute first,
        SchematicConnectionRoute second,
        Dictionary<string, List<SchematicRouteBridge>> bridges)
    {
        foreach ((Point start, Point end) in EnumerateSegments(first.Points))
        {
            foreach ((Point otherStart, Point otherEnd) in EnumerateSegments(second.Points))
            {
                if (!TryFindOrthogonalCrossing(start, end, otherStart, otherEnd, out Point crossing, out bool firstIsHorizontal))
                {
                    continue;
                }

                if (EndpointTouches(start, end, crossing) || EndpointTouches(otherStart, otherEnd, crossing))
                {
                    continue;
                }

                if (!firstIsHorizontal)
                {
                    continue;
                }

                if (!bridges.TryGetValue(first.Id, out List<SchematicRouteBridge>? list))
                {
                    list = [];
                    bridges[first.Id] = list;
                }

                if (!list.Any(bridge => SamePoint(bridge.Center, crossing)))
                {
                    list.Add(new SchematicRouteBridge(crossing, SchematicRouteBridgeOrientation.Horizontal));
                }
            }
        }
    }

    private static IEnumerable<(Point Start, Point End)> EnumerateSegments(IReadOnlyList<Point> points)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            yield return (points[index], points[index + 1]);
        }
    }

    private static bool TryFindOrthogonalCrossing(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd, out Point crossing, out bool firstIsHorizontal)
    {
        crossing = default;
        firstIsHorizontal = Math.Abs(firstStart.Y - firstEnd.Y) < 0.5;
        bool firstIsVertical = Math.Abs(firstStart.X - firstEnd.X) < 0.5;
        bool secondIsHorizontal = Math.Abs(secondStart.Y - secondEnd.Y) < 0.5;
        bool secondIsVertical = Math.Abs(secondStart.X - secondEnd.X) < 0.5;

        if (firstIsHorizontal && secondIsVertical)
        {
            crossing = new Point(secondStart.X, firstStart.Y);
            return Between(crossing.X, firstStart.X, firstEnd.X) && Between(crossing.Y, secondStart.Y, secondEnd.Y);
        }

        if (firstIsVertical && secondIsHorizontal)
        {
            firstIsHorizontal = false;
            crossing = new Point(firstStart.X, secondStart.Y);
            return Between(crossing.Y, firstStart.Y, firstEnd.Y) && Between(crossing.X, secondStart.X, secondEnd.X);
        }

        return false;
    }

    private static bool Between(double value, double start, double end) =>
        value > Math.Min(start, end) + 0.5 && value < Math.Max(start, end) - 0.5;

    private static bool EndpointTouches(Point start, Point end, Point point) =>
        SamePoint(start, point) || SamePoint(end, point);

    private static bool SamePoint(Point first, Point second) =>
        Math.Abs(first.X - second.X) < 0.5 && Math.Abs(first.Y - second.Y) < 0.5;

    private static Point Round(Point point) =>
        new(Math.Round(point.X, 1), Math.Round(point.Y, 1));

    private static string SourceNode(int index) => $"s{index.ToString(Invariant)}";

    private static string TargetNode(int index) => $"t{index.ToString(Invariant)}";

    private readonly record struct GraphvizCoordinateSpace(Rect Bounds)
    {
        public static GraphvizCoordinateSpace From(SchematicConnectionRoutingInput input)
        {
            Rect bounds = input.Layout.PanelRect.Inflate(180);
            foreach (SchematicConnectionRouteRequest request in input.Requests)
            {
                bounds = bounds.Union(new Rect(request.Source, new Size(1, 1)));
                bounds = bounds.Union(new Rect(request.Target, new Size(1, 1)));
            }

            if (input.Obstacles is not null)
            {
                foreach (Rect obstacle in input.Obstacles)
                {
                    bounds = bounds.Union(obstacle);
                }
            }

            return new GraphvizCoordinateSpace(bounds.Inflate(40));
        }

        public Point ToGraphviz(Point point) =>
            new((point.X - Bounds.X) / GraphvizScale, (Bounds.Bottom - point.Y) / GraphvizScale);

        public Point ToAvalonia(Point point) =>
            new(Bounds.X + point.X * GraphvizScale, Bounds.Bottom - point.Y * GraphvizScale);
    }

    private sealed record GraphvizPlainEdge(string Tail, string Head, IReadOnlyList<Point> Points)
    {
        public static string BuildKey(string tail, string head) => $"{tail}->{head}";
    }

    private static class GraphvizPlainParser
    {
        public static IReadOnlyDictionary<string, GraphvizPlainEdge> ParseEdges(string plain)
        {
            Dictionary<string, GraphvizPlainEdge> edges = new(StringComparer.Ordinal);
            using StringReader reader = new(plain);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length < 5 || !tokens[0].Equals("edge", StringComparison.Ordinal))
                {
                    continue;
                }

                string tail = tokens[1];
                string head = tokens[2];
                if (!int.TryParse(tokens[3], NumberStyles.Integer, Invariant, out int count) || tokens.Length < 4 + count * 2)
                {
                    continue;
                }

                Point[] points = new Point[count];
                for (int index = 0; index < count; index++)
                {
                    double x = double.Parse(tokens[4 + index * 2], Invariant);
                    double y = double.Parse(tokens[5 + index * 2], Invariant);
                    points[index] = new Point(x, y);
                }

                edges[GraphvizPlainEdge.BuildKey(tail, head)] = new GraphvizPlainEdge(tail, head, points);
            }

            return edges;
        }
    }
}
