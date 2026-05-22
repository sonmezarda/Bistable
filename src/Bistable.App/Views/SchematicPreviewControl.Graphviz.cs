using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
    private const double DotScale = 72.0;
    private static readonly CultureInfo DotCulture = CultureInfo.InvariantCulture;
    private string? _graphvizScopeCacheKey;
    private GraphvizPlainDiagram? _graphvizScopeCache;
    private string? _graphvizScopeError;

    private void DrawGraphvizDotScopePanel(
        DrawingContext context,
        Rect bounds,
        Rect moduleRect,
        IReadOnlyList<SignalViewModel> scopeSignals,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals)
    {
        Rect panel = new(
            Math.Max(16, bounds.X + 16),
            moduleRect.Bottom + 38,
            Math.Max(960, bounds.Width - 32),
            Math.Max(520, bounds.Height - moduleRect.Bottom - 70));
        _lastFocusedScopePanelRect = panel;

        context.FillRectangle(Palette.FocusPanelFill, panel, 8);
        context.DrawRectangle(new Pen(Palette.ModuleStroke, 1.2), panel, 8);
        DrawScopeExpansionButton(context, panel, ActiveScopePath, expanded: true);
        DrawText(context, string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!, panel.X + 16, panel.Y + 12, Palette.Text, 13);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, panel.Width - 32), panel.X + 16, panel.Y + 34, Palette.PinStroke, 11);
        if (!string.IsNullOrWhiteSpace(ActiveScopePath))
        {
            DrawText(context, Ellipsize(ActiveScopePath!, 10, panel.Width - 32), panel.X + 16, panel.Y + 52, Palette.Muted, 10);
        }

        GraphvizScopeGraph graph = BuildGraphvizScopeGraph(childScopes, scopePorts, localSignals);
        string key = graph.CacheKey;
        GraphvizPlainDiagram? diagram = null;
        string? error = null;
        if (string.Equals(_graphvizScopeCacheKey, key, StringComparison.Ordinal)
            && (_graphvizScopeCache is not null || _graphvizScopeError is not null))
        {
            diagram = _graphvizScopeCache;
            error = _graphvizScopeError;
        }
        else
        {
            try
            {
                diagram = RunGraphvizDot(graph.Dot);
                error = null;
            }
            catch (SchematicRoutingException exception)
            {
                diagram = null;
                error = exception.Message;
            }

            _graphvizScopeCacheKey = key;
            _graphvizScopeCache = diagram;
            _graphvizScopeError = error;
        }

        if (diagram is null)
        {
            DrawText(context, "GraphvizDot backend failed", panel.X + 18, panel.Y + 82, Palette.Selected, 12);
            DrawText(context, Ellipsize(error ?? "Unknown Graphviz error.", 10, panel.Width - 36), panel.X + 18, panel.Y + 104, Palette.Text, 10);
            return;
        }

        DrawGraphvizPlainDiagram(context, panel.Deflate(new Thickness(18, 82, 18, 34)), graph, diagram);
        DrawScopeProbeSummary(context, panel, scopeSignals);
    }

    private GraphvizScopeGraph BuildGraphvizScopeGraph(
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals)
    {
        GraphvizScopeBuilder builder = new(CompactLayout, IsScopeExpanded);
        string scopeTitle = string.IsNullOrWhiteSpace(ActiveScopeModuleName) ? ModuleName : ActiveScopeModuleName!;
        builder.AddScopeBoundary(scopeTitle, scopePorts);
        foreach (HierarchyScopeLocalSignalViewModel local in localSignals)
        {
            builder.AddLocal(local);
        }

        foreach (HierarchyScopeInstanceViewModel child in OrderChildScopesForLayout(childScopes, scopePorts))
        {
            builder.AddInstance(child);
        }

        builder.AddScopeConnections(childScopes, scopePorts, localSignals);
        return builder.Build();
    }

    private static GraphvizPlainDiagram RunGraphvizDot(string dot)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dot",
            Arguments = "-Tplain",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new SchematicRoutingException("Graphviz dot could not be started.");
            process.StandardInput.Write(dot);
            process.StandardInput.Close();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                throw new SchematicRoutingException("Graphviz dot timed out after 5 seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new SchematicRoutingException(
                    string.IsNullOrWhiteSpace(error)
                        ? $"Graphviz dot exited with code {process.ExitCode}."
                        : $"Graphviz dot exited with code {process.ExitCode}: {error.Trim()}");
            }

            return GraphvizPlainDiagram.Parse(output);
        }
        catch (Win32Exception exception)
        {
            throw new SchematicRoutingException("Graphviz dot executable was not found. Install Graphviz to use GraphvizDot schematic rendering.", exception);
        }
    }

    private void DrawGraphvizPlainDiagram(
        DrawingContext context,
        Rect viewport,
        GraphvizScopeGraph graph,
        GraphvizPlainDiagram diagram)
    {
        double rawWidth = Math.Max(1, diagram.Width * DotScale);
        double rawHeight = Math.Max(1, diagram.Height * DotScale);
        double scale = Math.Min(viewport.Width / rawWidth, viewport.Height / rawHeight);
        scale = Math.Clamp(scale, 0.18, 1.65);
        double renderedWidth = rawWidth * scale;
        double renderedHeight = rawHeight * scale;
        Point origin = new(
            viewport.X + Math.Max(0, (viewport.Width - renderedWidth) / 2),
            viewport.Y + Math.Max(0, (viewport.Height - renderedHeight) / 2));

        Point Map(Point point) =>
            new(origin.X + point.X * DotScale * scale, origin.Y + (diagram.Height - point.Y) * DotScale * scale);

        Dictionary<string, Rect> nodeRects = new(StringComparer.Ordinal);
        foreach (GraphvizPlainNode node in diagram.Nodes)
        {
            Point center = Map(node.Position);
            Size size = new(node.Width * DotScale * scale, node.Height * DotScale * scale);
            nodeRects[node.Id] = new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
        }

        NormalizeGraphvizModuleRects(graph, nodeRects);
        NormalizeRootBoundaryPortRects(graph, nodeRects, viewport);
        DrawGraphvizContainers(context, graph, nodeRects);
        IReadOnlyList<Rect> routeObstacles = BuildGraphvizRouteObstacles(graph, nodeRects);

        List<GraphvizRoutableEdge> routableEdges = [];
        foreach (GraphvizEdgeDefinition definition in graph.Edges.Values)
        {
            if (TryResolveGraphvizEdgeAnchors(definition, graph.Nodes, nodeRects, out Point start, out Point end))
            {
                routableEdges.Add(new GraphvizRoutableEdge(definition, start, end, BuildGraphvizLaneGroupKey(start, end)));
            }
        }

        Dictionary<string, int> laneCounts = routableEdges
            .GroupBy(static edge => edge.LaneGroupKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, int> laneIndexes = new(StringComparer.Ordinal);
        List<GraphvizRenderedEdge> renderedEdges = [];
        List<GraphvizOccupiedSegment> occupiedSegments = [];
        foreach (GraphvizRoutableEdge edge in routableEdges
                     .OrderBy(static edge => Math.Min(edge.Start.Y, edge.End.Y))
                     .ThenBy(static edge => Math.Max(edge.Start.Y, edge.End.Y))
                     .ThenBy(static edge => edge.Definition.Id, StringComparer.Ordinal))
        {
            laneIndexes.TryGetValue(edge.LaneGroupKey, out int laneIndex);
            laneIndexes[edge.LaneGroupKey] = laneIndex + 1;
            int laneCount = laneCounts.TryGetValue(edge.LaneGroupKey, out int count) ? count : 1;
            IReadOnlyList<Point> route = BuildGraphvizRenderedRoute(
                edge.Start,
                edge.End,
                laneIndex,
                laneCount,
                routeObstacles,
                occupiedSegments,
                edge.Definition.SelectionSignalName);
            renderedEdges.Add(new GraphvizRenderedEdge(edge.Definition, route));
            AddGraphvizOccupiedSegments(occupiedSegments, route, edge.Definition.SelectionSignalName);
        }

        foreach (GraphvizRenderedEdge renderedEdge in renderedEdges)
        {
            GraphvizEdgeDefinition definition = renderedEdge.Definition;
            IReadOnlyList<Point> routePoints = renderedEdge.Points;
            bool highlighted = IsGraphvizSignalHighlighted(definition);
            IBrush brush = highlighted
                ? Palette.Selected
                : definition.Kind == GraphvizEdgeKind.Input
                ? Palette.PinStroke
                : definition.Kind == GraphvizEdgeKind.Output
                    ? Palette.OutputValue
                    : Palette.LocalNet;
            Pen pen = new(brush, highlighted ? 3.0 : definition.Width > 1 ? 1.8 : 1.2);
            for (int index = 0; index < routePoints.Count - 1; index++)
            {
                context.DrawLine(pen, routePoints[index], routePoints[index + 1]);
            }

            if (!definition.SuppressLabel)
            {
                DrawGraphvizEdgeLabel(context, routePoints, definition, brush);
            }

            if (!string.IsNullOrWhiteSpace(definition.SelectionSignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(definition.SelectionSignalName!, null, routePoints));
            }
        }

        DrawGraphvizJunctionDots(context, renderedEdges);

        foreach (GraphvizPlainNode node in diagram.Nodes)
        {
            if (!graph.Nodes.TryGetValue(node.Id, out GraphvizNodeDefinition? definition))
            {
                continue;
            }

            DrawGraphvizNode(context, nodeRects[node.Id], definition);
        }
    }

    private static IReadOnlyList<Rect> BuildGraphvizRouteObstacles(
        GraphvizScopeGraph graph,
        IReadOnlyDictionary<string, Rect> nodeRects)
    {
        List<Rect> obstacles = [];
        foreach (GraphvizNodeDefinition node in graph.Nodes.Values)
        {
            if (node.Kind != GraphvizNodeKind.Module || !nodeRects.TryGetValue(node.Id, out Rect rect))
            {
                continue;
            }

            obstacles.Add(rect.Inflate(new Thickness(10)));
        }

        return obstacles;
    }

    private static void NormalizeGraphvizModuleRects(GraphvizScopeGraph graph, IDictionary<string, Rect> nodeRects)
    {
        foreach (GraphvizNodeDefinition node in graph.Nodes.Values)
        {
            if (node.Kind != GraphvizNodeKind.Module || !nodeRects.TryGetValue(node.Id, out Rect rect))
            {
                continue;
            }

            int rows = Math.Max(node.InputLabels.Count, node.OutputLabels.Count);
            if (rows == 0)
            {
                continue;
            }

            double requiredHeight = GraphvizModuleRequiredHeight(rows);
            double requiredWidth = Math.Max(230, 20 + Math.Max(
                node.InputLabels.DefaultIfEmpty(string.Empty).Max(label => MeasureWidth(label, 9)),
                node.OutputLabels.DefaultIfEmpty(string.Empty).Max(label => MeasureWidth(label, 9))) * 2.4);
            double width = Math.Max(rect.Width, requiredWidth);
            double height = Math.Max(rect.Height, requiredHeight);
            nodeRects[node.Id] = new Rect(
                rect.Center.X - width / 2,
                rect.Center.Y - height / 2,
                width,
                height);
        }
    }

    private static void NormalizeRootBoundaryPortRects(
        GraphvizScopeGraph graph,
        IDictionary<string, Rect> nodeRects,
        Rect viewport)
    {
        foreach (GraphvizNodeDefinition node in graph.Nodes.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.ContainerPath)
                || node.Kind is not (GraphvizNodeKind.InputPort or GraphvizNodeKind.OutputPort)
                || !nodeRects.TryGetValue(node.Id, out Rect rect))
            {
                continue;
            }

            double x = node.Kind == GraphvizNodeKind.InputPort
                ? viewport.X + 4
                : viewport.Right - rect.Width - 4;
            nodeRects[node.Id] = new Rect(x, rect.Y, rect.Width, rect.Height);
        }
    }

    private void DrawGraphvizContainers(
        DrawingContext context,
        GraphvizScopeGraph graph,
        IReadOnlyDictionary<string, Rect> nodeRects)
    {
        foreach (GraphvizContainerDefinition container in graph.Containers)
        {
            Rect? bounds = null;
            double? inputEdge = null;
            double? outputEdge = null;
            foreach (GraphvizNodeDefinition node in graph.Nodes.Values.Where(node => string.Equals(node.ContainerPath, container.ScopePath, StringComparison.OrdinalIgnoreCase)))
            {
                if (!nodeRects.TryGetValue(node.Id, out Rect rect))
                {
                    continue;
                }

                bounds = bounds is null ? rect : bounds.Value.Union(rect);
                if (node.Kind == GraphvizNodeKind.InputPort)
                {
                    inputEdge = inputEdge is null ? rect.Center.X : Math.Min(inputEdge.Value, rect.Center.X);
                }
                else if (node.Kind == GraphvizNodeKind.OutputPort)
                {
                    outputEdge = outputEdge is null ? rect.Center.X : Math.Max(outputEdge.Value, rect.Center.X);
                }
            }

            if (bounds is null)
            {
                continue;
            }

            Rect content = bounds.Value;
            double left = inputEdge ?? content.X - 28;
            double right = outputEdge ?? content.Right + 28;
            double top = content.Y - 44;
            double bottom = content.Bottom + 28;
            Rect frame = new(left, top, Math.Max(140, right - left), Math.Max(90, bottom - top));
            context.FillRectangle(Palette.FocusPanelFill, frame, 8);
            context.DrawRectangle(new Pen(Palette.PinStroke, 1.2), frame, 8);
            DrawText(context, container.Title, frame.X + 14, frame.Y + 10, Palette.Text, 12);
            DrawText(context, container.ModuleName, frame.X + 14, frame.Y + 30, Palette.PinStroke, 10);
            DrawScopeExpansionButton(context, frame, container.ScopePath, expanded: true);
        }
    }

    private static bool TryResolveGraphvizEdgeAnchors(
        GraphvizEdgeDefinition edge,
        IReadOnlyDictionary<string, GraphvizNodeDefinition> nodes,
        IReadOnlyDictionary<string, Rect> nodeRects,
        out Point start,
        out Point end)
    {
        start = default;
        end = default;
        if (!nodes.TryGetValue(edge.Tail, out GraphvizNodeDefinition? tailNode)
            || !nodes.TryGetValue(edge.Head, out GraphvizNodeDefinition? headNode)
            || !nodeRects.TryGetValue(edge.Tail, out Rect tailRect)
            || !nodeRects.TryGetValue(edge.Head, out Rect headRect))
        {
            return false;
        }

        start = ResolveGraphvizAnchor(tailNode, tailRect, edge.TailPortName, isTail: true);
        end = ResolveGraphvizAnchor(headNode, headRect, edge.HeadPortName, isTail: false);
        return true;
    }

    private static IReadOnlyList<Point> BuildGraphvizRenderedRoute(
        Point start,
        Point end,
        int laneIndex,
        int laneCount,
        IReadOnlyList<Rect> obstacles,
        IReadOnlyList<GraphvizOccupiedSegment> occupiedSegments,
        string? signalName)
    {
        const double minimumLeg = 28;
        const double laneSpacing = 19;
        Point escapedStart = new(start.X + 16, start.Y);
        Point escapedEnd = new(end.X - 16, end.Y);
        double routeX;
        if (escapedStart.X <= escapedEnd.X)
        {
            double minX = escapedStart.X + minimumLeg;
            double maxX = escapedEnd.X - minimumLeg;
            double centeredLaneOffset = (laneIndex - (laneCount - 1) / 2.0) * laneSpacing;
            routeX = minX <= maxX
                ? Math.Clamp((escapedStart.X + escapedEnd.X) / 2 + centeredLaneOffset, minX, maxX)
                : (escapedStart.X + escapedEnd.X) / 2;
        }
        else
        {
            routeX = Math.Max(escapedStart.X, escapedEnd.X) + minimumLeg + laneIndex * laneSpacing;
        }

        IReadOnlyList<Point> middle = TryBuildGraphvizObstacleAwareRoute(
            escapedStart,
            escapedEnd,
            routeX,
            laneIndex,
            obstacles,
            occupiedSegments,
            signalName);
        List<Point> route = new(middle.Count + 2) { start };
        route.AddRange(middle);
        route.Add(end);
        return SimplifyRoutePoints(route);
    }

    private static IReadOnlyList<Point> TryBuildGraphvizObstacleAwareRoute(
        Point start,
        Point end,
        double preferredX,
        int laneIndex,
        IReadOnlyList<Rect> obstacles,
        IReadOnlyList<GraphvizOccupiedSegment> occupiedSegments,
        string? signalName)
    {
        IReadOnlyList<Point>? routed = BuildGraphvizSparseGridRoute(start, end, preferredX, laneIndex, obstacles, occupiedSegments, signalName);
        if (routed is not null)
        {
            return routed;
        }

        return SimplifyRoutePoints(
        [
            start,
            new Point(preferredX, start.Y),
            new Point(preferredX, end.Y),
            end
        ]);
    }

    private static IReadOnlyList<Point>? BuildGraphvizSparseGridRoute(
        Point start,
        Point end,
        double preferredX,
        int laneIndex,
        IReadOnlyList<Rect> obstacles,
        IReadOnlyList<GraphvizOccupiedSegment> occupiedSegments,
        string? signalName)
    {
        const double outerMargin = 34;
        const double laneSpacing = 17;
        SortedSet<double> xs = [];
        SortedSet<double> ys = [];
        AddCoordinate(xs, start.X);
        AddCoordinate(xs, end.X);
        AddCoordinate(xs, preferredX);
        AddCoordinate(ys, start.Y);
        AddCoordinate(ys, end.Y);

        double laneOffset = laneIndex * laneSpacing;
        foreach (Rect obstacle in obstacles)
        {
            AddCoordinate(xs, obstacle.X - outerMargin - laneOffset);
            AddCoordinate(xs, obstacle.X - outerMargin);
            AddCoordinate(xs, obstacle.Right + outerMargin);
            AddCoordinate(xs, obstacle.Right + outerMargin + laneOffset);
            AddCoordinate(ys, obstacle.Y - outerMargin - laneOffset);
            AddCoordinate(ys, obstacle.Y - outerMargin);
            AddCoordinate(ys, obstacle.Bottom + outerMargin);
            AddCoordinate(ys, obstacle.Bottom + outerMargin + laneOffset);
        }

        double[] xValues = xs.ToArray();
        double[] yValues = ys.ToArray();
        GraphvizGridPoint source = new(Array.IndexOf(xValues, RoundCoordinate(start.X)), Array.IndexOf(yValues, RoundCoordinate(start.Y)));
        GraphvizGridPoint target = new(Array.IndexOf(xValues, RoundCoordinate(end.X)), Array.IndexOf(yValues, RoundCoordinate(end.Y)));
        if (source.X < 0 || source.Y < 0 || target.X < 0 || target.Y < 0)
        {
            return null;
        }

        PriorityQueue<GraphvizGridPoint, double> frontier = new();
        Dictionary<GraphvizGridPoint, GraphvizGridPoint> cameFrom = [];
        Dictionary<GraphvizGridPoint, double> costSoFar = [];
        frontier.Enqueue(source, 0);
        costSoFar[source] = 0;

        while (frontier.Count > 0)
        {
            GraphvizGridPoint current = frontier.Dequeue();
            if (current.Equals(target))
            {
                break;
            }

            foreach (GraphvizGridPoint next in EnumerateGraphvizGridNeighbors(current, xValues.Length, yValues.Length))
            {
                Point currentPoint = new(xValues[current.X], yValues[current.Y]);
                Point nextPoint = new(xValues[next.X], yValues[next.Y]);
                if (GraphvizSegmentHitsObstacle(currentPoint, nextPoint, obstacles))
                {
                    continue;
                }

                if (GraphvizSegmentOverlapsOccupiedSignal(currentPoint, nextPoint, occupiedSegments, signalName))
                {
                    continue;
                }

                double newCost = costSoFar[current] + Math.Abs(currentPoint.X - nextPoint.X) + Math.Abs(currentPoint.Y - nextPoint.Y);
                if (Math.Abs(nextPoint.X - preferredX) > 0.5)
                {
                    newCost += Math.Abs(nextPoint.X - preferredX) * 0.18;
                }

                if (!costSoFar.TryGetValue(next, out double existingCost) || newCost < existingCost)
                {
                    costSoFar[next] = newCost;
                    double priority = newCost + Math.Abs(nextPoint.X - end.X) + Math.Abs(nextPoint.Y - end.Y);
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }

        if (!cameFrom.ContainsKey(target) && !source.Equals(target))
        {
            return null;
        }

        List<Point> route = [];
        GraphvizGridPoint cursor = target;
        route.Add(new Point(xValues[cursor.X], yValues[cursor.Y]));
        while (!cursor.Equals(source))
        {
            cursor = cameFrom[cursor];
            route.Add(new Point(xValues[cursor.X], yValues[cursor.Y]));
        }

        route.Reverse();
        return SimplifyRoutePoints(route);
    }

    private static IEnumerable<GraphvizGridPoint> EnumerateGraphvizGridNeighbors(GraphvizGridPoint point, int width, int height)
    {
        if (point.X > 0)
        {
            yield return point with { X = point.X - 1 };
        }

        if (point.X < width - 1)
        {
            yield return point with { X = point.X + 1 };
        }

        if (point.Y > 0)
        {
            yield return point with { Y = point.Y - 1 };
        }

        if (point.Y < height - 1)
        {
            yield return point with { Y = point.Y + 1 };
        }
    }

    private static bool GraphvizSegmentHitsObstacle(Point start, Point end, IReadOnlyList<Rect> obstacles)
    {
        bool horizontal = Math.Abs(start.Y - end.Y) < 0.5;
        bool vertical = Math.Abs(start.X - end.X) < 0.5;
        if (!horizontal && !vertical)
        {
            return true;
        }

        foreach (Rect obstacle in obstacles)
        {
            if (horizontal)
            {
                double minX = Math.Min(start.X, end.X);
                double maxX = Math.Max(start.X, end.X);
                if (start.Y > obstacle.Y
                    && start.Y < obstacle.Bottom
                    && maxX > obstacle.X
                    && minX < obstacle.Right)
                {
                    return true;
                }
            }
            else
            {
                double minY = Math.Min(start.Y, end.Y);
                double maxY = Math.Max(start.Y, end.Y);
                if (start.X > obstacle.X
                    && start.X < obstacle.Right
                    && maxY > obstacle.Y
                    && minY < obstacle.Bottom)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool GraphvizSegmentOverlapsOccupiedSignal(
        Point start,
        Point end,
        IReadOnlyList<GraphvizOccupiedSegment> occupiedSegments,
        string? signalName)
    {
        foreach (GraphvizOccupiedSegment occupied in occupiedSegments)
        {
            if (string.Equals(signalName, occupied.SignalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (GraphvizSegmentsOverlap(start, end, occupied.Start, occupied.End))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GraphvizSegmentsOverlap(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd)
    {
        bool firstHorizontal = Math.Abs(firstStart.Y - firstEnd.Y) < 0.5;
        bool secondHorizontal = Math.Abs(secondStart.Y - secondEnd.Y) < 0.5;
        if (firstHorizontal != secondHorizontal)
        {
            return false;
        }

        if (firstHorizontal)
        {
            if (Math.Abs(firstStart.Y - secondStart.Y) > 0.5)
            {
                return false;
            }

            return RangesOverlap(firstStart.X, firstEnd.X, secondStart.X, secondEnd.X);
        }

        if (Math.Abs(firstStart.X - secondStart.X) > 0.5)
        {
            return false;
        }

        return RangesOverlap(firstStart.Y, firstEnd.Y, secondStart.Y, secondEnd.Y);
    }

    private static bool RangesOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        double firstMin = Math.Min(firstStart, firstEnd);
        double firstMax = Math.Max(firstStart, firstEnd);
        double secondMin = Math.Min(secondStart, secondEnd);
        double secondMax = Math.Max(secondStart, secondEnd);
        return Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin) > 2;
    }

    private static void AddGraphvizOccupiedSegments(
        ICollection<GraphvizOccupiedSegment> occupiedSegments,
        IReadOnlyList<Point> route,
        string? signalName)
    {
        for (int index = 0; index < route.Count - 1; index++)
        {
            occupiedSegments.Add(new GraphvizOccupiedSegment(route[index], route[index + 1], signalName));
        }
    }

    private static void AddCoordinate(ISet<double> coordinates, double value) => coordinates.Add(RoundCoordinate(value));

    private static double RoundCoordinate(double value) => Math.Round(value, 1);

    private static string BuildGraphvizLaneGroupKey(Point start, Point end)
    {
        double left = Math.Min(start.X, end.X);
        double right = Math.Max(start.X, end.X);
        string direction = start.X <= end.X ? "lr" : "rl";
        return string.Create(
            DotCulture,
            $"{direction}:{Math.Round(left / 24)}:{Math.Round(right / 24)}");
    }

    private static Point ResolveGraphvizAnchor(GraphvizNodeDefinition node, Rect rect, string? portName, bool isTail)
    {
        if (!string.IsNullOrWhiteSpace(node.ContainerPath)
            && node.Kind is GraphvizNodeKind.InputPort or GraphvizNodeKind.OutputPort)
        {
            return new Point(rect.Center.X, rect.Center.Y);
        }

        return node.Kind switch
        {
            GraphvizNodeKind.Module => ResolveGraphvizModuleAnchor(node, rect, portName, isTail),
            GraphvizNodeKind.InputPort or GraphvizNodeKind.OutputPort => isTail
                ? new Point(rect.Right, rect.Center.Y)
                : new Point(rect.X, rect.Center.Y),
            GraphvizNodeKind.Local => isTail ? new Point(rect.Right, rect.Center.Y) : new Point(rect.X, rect.Center.Y),
            _ => isTail ? new Point(rect.Right, rect.Center.Y) : new Point(rect.X, rect.Center.Y)
        };
    }

    private static Point ResolveGraphvizModuleAnchor(GraphvizNodeDefinition node, Rect rect, string? portName, bool isTail)
    {
        bool output = isTail;
        IReadOnlyList<string> labels = output ? node.OutputLabels : node.InputLabels;
        int index = FindPortLabelIndex(labels, portName);
        int rows = Math.Max(node.InputLabels.Count, node.OutputLabels.Count);
        double y = labels.Count == 0
            ? rect.Center.Y
            : GraphvizModulePortY(rect, rows, Math.Clamp(index, 0, labels.Count - 1));
        return output ? new Point(rect.Right, y) : new Point(rect.X, y);
    }

    private static double GraphvizModuleHeaderY(Rect rect) => rect.Y + 42;

    private static double GraphvizModuleRequiredHeight(int rows) => 76 + rows * 26;

    private static double GraphvizModuleRowHeight(Rect rect, int rows)
    {
        double headerY = GraphvizModuleHeaderY(rect);
        return Math.Max(18, Math.Min(26, (rect.Bottom - headerY - 24) / Math.Max(1, rows)));
    }

    private static double GraphvizModulePortY(Rect rect, int rows, int index) =>
        GraphvizModuleHeaderY(rect) + 12 + index * GraphvizModuleRowHeight(rect, rows);

    private static int FindPortLabelIndex(IReadOnlyList<string> labels, string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return 0;
        }

        for (int index = 0; index < labels.Count; index++)
        {
            if (string.Equals(ExtractPortName(labels[index]), portName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static string ExtractPortName(string label)
    {
        int widthStart = label.IndexOf(" [", StringComparison.Ordinal);
        return widthStart > 0 ? label[..widthStart] : label;
    }

    private static IReadOnlyList<Point> SimplifyRoutePoints(IReadOnlyList<Point> points)
    {
        List<Point> compact = [];
        foreach (Point point in points)
        {
            if (compact.Count == 0 || Math.Abs(compact[^1].X - point.X) > 0.5 || Math.Abs(compact[^1].Y - point.Y) > 0.5)
            {
                compact.Add(point);
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

    private void DrawGraphvizEdgeLabel(
        DrawingContext context,
        IReadOnlyList<Point> points,
        GraphvizEdgeDefinition edge,
        IBrush brush)
    {
        if (points.Count < 2)
        {
            return;
        }

        (Point start, Point end) = FindLongestSegment(points);
        Point mid = new((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        string label = edge.Width <= 1 ? edge.SignalName : $"{edge.SignalName} [{edge.Width}b]";
        double width = Math.Min(150, MeasureWidth(label, 9) + 10);
        Rect rect = new(mid.X - width / 2, mid.Y - 18, width, 15);
        context.FillRectangle(Palette.FocusPanelFill, rect, 3);
        DrawText(context, Ellipsize(label, 9, rect.Width - 6), rect.X + 3, rect.Y + 1, brush, 9);
    }

    private bool IsGraphvizSignalHighlighted(GraphvizEdgeDefinition edge)
    {
        string? selected = SelectedSignalName;
        string? candidate = edge.SelectionSignalName ?? edge.SignalName;
        return !string.IsNullOrWhiteSpace(candidate)
            && (string.Equals(candidate, selected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, _hoveredSignalName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.SignalName, selected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.SignalName, _hoveredSignalName, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawGraphvizJunctionDots(DrawingContext context, IReadOnlyList<GraphvizRenderedEdge> edges)
    {
        foreach (IGrouping<string, GraphvizRenderedEdge> group in edges
                     .Where(static edge => !string.IsNullOrWhiteSpace(edge.Definition.SelectionSignalName))
                     .GroupBy(static edge => edge.Definition.SelectionSignalName!, StringComparer.OrdinalIgnoreCase))
        {
            Dictionary<string, (Point Point, int Count, GraphvizEdgeDefinition Definition)> points = new(StringComparer.Ordinal);
            foreach (GraphvizRenderedEdge edge in group)
            {
                foreach (Point point in edge.Points)
                {
                    string key = $"{Math.Round(point.X)}:{Math.Round(point.Y)}";
                    points[key] = points.TryGetValue(key, out (Point Point, int Count, GraphvizEdgeDefinition Definition) current)
                        ? (current.Point, current.Count + 1, current.Definition)
                        : (point, 1, edge.Definition);
                }
            }

            foreach ((Point point, int count, GraphvizEdgeDefinition definition) in points.Values)
            {
                if (count < 2)
                {
                    continue;
                }

                IBrush brush = IsGraphvizSignalHighlighted(definition)
                    ? Palette.Selected
                    : definition.Kind == GraphvizEdgeKind.Output
                        ? Palette.OutputValue
                        : definition.Kind == GraphvizEdgeKind.Local
                            ? Palette.LocalNet
                            : Palette.PinStroke;
                context.DrawEllipse(brush, null, point, 3.5, 3.5);
            }
        }
    }

    private static (Point Start, Point End) FindLongestSegment(IReadOnlyList<Point> points)
    {
        Point bestStart = points[0];
        Point bestEnd = points[1];
        double bestLength = 0;
        for (int index = 0; index < points.Count - 1; index++)
        {
            Point start = points[index];
            Point end = points[index + 1];
            double length = Math.Abs(start.X - end.X) + Math.Abs(start.Y - end.Y);
            if (length > bestLength)
            {
                bestLength = length;
                bestStart = start;
                bestEnd = end;
            }
        }

        return (bestStart, bestEnd);
    }

    private void DrawGraphvizNode(DrawingContext context, Rect rect, GraphvizNodeDefinition node)
    {
        if (node.Kind is GraphvizNodeKind.InputPort or GraphvizNodeKind.OutputPort)
        {
            DrawGraphvizBoundaryPortNode(context, rect, node);
            return;
        }

        if (node.Kind is GraphvizNodeKind.Local)
        {
            DrawGraphvizLocalNode(context, rect, node);
            return;
        }

        IBrush stroke = node.Kind switch
        {
            GraphvizNodeKind.InputPort => Palette.PinStroke,
            GraphvizNodeKind.OutputPort => Palette.OutputValue,
            GraphvizNodeKind.Local => Palette.LocalNet,
            _ => node.Expanded ? Palette.Selected : Palette.ModuleStroke
        };
        IBrush fill = node.Kind is GraphvizNodeKind.Module ? Palette.NodeFill : Palette.ValueFill;
        float radius = node.Kind is GraphvizNodeKind.Module ? 6 : 4;
        context.FillRectangle(fill, rect, radius);
        context.DrawRectangle(new Pen(stroke, node.Expanded ? 1.4 : 1.0), rect, radius);
        DrawText(context, Ellipsize(node.Title, 11, rect.Width - 12), rect.X + 6, rect.Y + 6, node.Kind is GraphvizNodeKind.OutputPort ? Palette.OutputValue : Palette.Text, 11);
        if (!string.IsNullOrWhiteSpace(node.Subtitle) && rect.Height > 38)
        {
            DrawText(context, Ellipsize(node.Subtitle!, 9, rect.Width - 12), rect.X + 6, rect.Y + 24, Palette.Muted, 9);
        }

        if (node.InputLabels.Count > 0 || node.OutputLabels.Count > 0)
        {
            double headerY = rect.Y + 42;
            context.DrawLine(new Pen(Palette.ModuleStroke, 1), new Point(rect.X + 8, headerY), new Point(rect.Right - 8, headerY));
            int rows = Math.Max(node.InputLabels.Count, node.OutputLabels.Count);
            for (int index = 0; index < node.InputLabels.Count; index++)
            {
                double y = GraphvizModulePortY(rect, rows, index);
                context.DrawLine(new Pen(Palette.PinStroke, 1.0), new Point(rect.X - 7, y), new Point(rect.X + 2, y));
                context.FillRectangle(Palette.PinStroke, new Rect(rect.X - 2, y - 2, 4, 4));
                DrawText(context, Ellipsize(node.InputLabels[index], 9, rect.Width * 0.42), rect.X + 10, y - 6, Palette.Text, 9);
            }

            for (int index = 0; index < node.OutputLabels.Count; index++)
            {
                string output = Ellipsize(node.OutputLabels[index], 9, rect.Width * 0.42);
                double y = GraphvizModulePortY(rect, rows, index);
                context.DrawLine(new Pen(Palette.OutputValue, 1.0), new Point(rect.Right - 2, y), new Point(rect.Right + 7, y));
                context.FillRectangle(Palette.OutputValue, new Rect(rect.Right - 2, y - 2, 4, 4));
                DrawText(context, output, rect.Right - 10 - MeasureWidth(output, 9), y - 6, Palette.OutputValue, 9);
            }
        }

        if (!string.IsNullOrWhiteSpace(node.WidthLabel))
        {
            DrawText(context, node.WidthLabel!, rect.Right - Math.Min(46, rect.Width / 2), rect.Bottom - 17, stroke, 9);
        }

        if (!string.IsNullOrWhiteSpace(node.ScopePath) && node.CanExpand)
        {
            Rect button = new(rect.Right - 20, rect.Y + 5, 15, 15);
            context.FillRectangle(Palette.ValueFill, button, 3);
            context.DrawRectangle(new Pen(node.Expanded ? Palette.Selected : Palette.PinStroke, 1), button, 3);
            DrawText(context, node.Expanded ? "-" : "+", button.X + 4, button.Y - 1, node.Expanded ? Palette.Selected : Palette.PinStroke, 11);
            _expansionHitTargets.Add(new ExpansionHitTarget(node.ScopePath!, button.Inflate(5)));
        }

        if (!string.IsNullOrWhiteSpace(node.SignalName))
        {
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(node.SignalName!, rect, null));
        }
    }

    private void DrawGraphvizBoundaryPortNode(DrawingContext context, Rect rect, GraphvizNodeDefinition node)
    {
        bool input = node.Kind == GraphvizNodeKind.InputPort;
        IBrush stroke = input ? Palette.PinStroke : Palette.OutputValue;
        if (!string.IsNullOrWhiteSpace(node.ContainerPath))
        {
            double pinY = rect.Center.Y;
            context.DrawLine(new Pen(stroke, 1.2), new Point(rect.X, pinY), new Point(rect.Right, pinY));
            context.FillRectangle(stroke, new Rect(rect.Center.X - 2, pinY - 2, 4, 4));
            if (!string.IsNullOrWhiteSpace(node.SignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(node.SignalName!, rect.Inflate(5), null));
            }

            return;
        }

        double centerY = rect.Center.Y;
        double glyphWidth = Math.Max(14, Math.Min(20, rect.Width));
        Point origin = input
            ? new Point(rect.X, centerY)
            : new Point(rect.Right - glyphWidth, centerY);
        Point[] points = input
            ?
            [
                new Point(origin.X, centerY - 7),
                new Point(origin.X + glyphWidth * 0.68, centerY - 7),
                new Point(origin.X + glyphWidth, centerY),
                new Point(origin.X + glyphWidth * 0.68, centerY + 7),
                new Point(origin.X, centerY + 7)
            ]
            :
            [
                new Point(origin.X + glyphWidth, centerY - 7),
                new Point(origin.X + glyphWidth * 0.32, centerY - 7),
                new Point(origin.X, centerY),
                new Point(origin.X + glyphWidth * 0.32, centerY + 7),
                new Point(origin.X + glyphWidth, centerY + 7)
            ];
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], isFilled: true);
            for (int index = 1; index < points.Length; index++)
            {
                geometryContext.LineTo(points[index]);
            }

            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(Palette.ValueFill, new Pen(stroke, 1.1), geometry);
        double labelWidth = Math.Min(120, Math.Max(30, MeasureWidth(node.Title, 10)));
        double textX = input ? origin.X + glyphWidth + 6 : origin.X - labelWidth - 6;
        DrawText(context, Ellipsize(node.Title, 10, labelWidth), textX, rect.Y - 14, Palette.Text, 10);
        if (!string.IsNullOrWhiteSpace(node.WidthLabel))
        {
            DrawText(context, node.WidthLabel!, textX, rect.Y + 2, stroke, 8);
        }

        if (!string.IsNullOrWhiteSpace(node.SignalName))
        {
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(node.SignalName!, rect, null));
        }
    }

    private void DrawGraphvizLocalNode(DrawingContext context, Rect rect, GraphvizNodeDefinition node)
    {
        context.FillRectangle(Palette.ValueFill, rect, 5);
        context.DrawRectangle(new Pen(Palette.LocalNet, 1), rect, 5);
        DrawText(context, Ellipsize(node.Title, 10, rect.Width - 42), rect.X + 6, rect.Y + 4, Palette.Text, 10);
        if (!string.IsNullOrWhiteSpace(node.WidthLabel))
        {
            DrawText(context, node.WidthLabel!, rect.Right - 34, rect.Y + 4, Palette.LocalNet, 8);
        }

        if (!string.IsNullOrWhiteSpace(node.SignalName))
        {
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(node.SignalName!, rect, null));
        }
    }

    private void DrawScopeProbeSummary(DrawingContext context, Rect panel, IReadOnlyList<SignalViewModel> scopeSignals)
    {
        string summary = scopeSignals.Count == 0
            ? "No exact-scope probes are available."
            : $"{scopeSignals.Count} exact-scope traced signals.";
        DrawText(context, summary, panel.X + 16, panel.Bottom - 22, Palette.Muted, 10);
    }

    private enum GraphvizNodeKind
    {
        Module,
        InputPort,
        OutputPort,
        Local
    }

    private enum GraphvizEdgeKind
    {
        Input,
        Output,
        Local
    }

    private sealed record GraphvizNodeDefinition(
        string Id,
        string Title,
        string? Subtitle,
        GraphvizNodeKind Kind,
        string? WidthLabel = null,
        string? SignalName = null,
        string? ScopePath = null,
        bool CanExpand = false,
        bool Expanded = false,
        IReadOnlyList<string>? InputLabels = null,
        IReadOnlyList<string>? OutputLabels = null,
        string? ContainerPath = null)
    {
        public IReadOnlyList<string> InputLabels { get; } = InputLabels ?? [];

        public IReadOnlyList<string> OutputLabels { get; } = OutputLabels ?? [];
    }

    private sealed record GraphvizEdgeDefinition(
        string Id,
        string Tail,
        string Head,
        string TailRef,
        string HeadRef,
        string SignalName,
        string? SelectionSignalName,
        int Width,
        GraphvizEdgeKind Kind,
        bool SuppressLabel = false,
        string? TailPortName = null,
        string? HeadPortName = null);

    private sealed record GraphvizRoutableEdge(GraphvizEdgeDefinition Definition, Point Start, Point End, string LaneGroupKey);

    private sealed record GraphvizRenderedEdge(GraphvizEdgeDefinition Definition, IReadOnlyList<Point> Points);

    private sealed record GraphvizOccupiedSegment(Point Start, Point End, string? SignalName);

    private readonly record struct GraphvizGridPoint(int X, int Y);

    private sealed record GraphvizContainerDefinition(string ScopePath, string Title, string ModuleName);

    private sealed record GraphvizScopeGraph(
        string Dot,
        string CacheKey,
        IReadOnlyDictionary<string, GraphvizNodeDefinition> Nodes,
        IReadOnlyDictionary<string, GraphvizEdgeDefinition> Edges,
        IReadOnlyList<GraphvizContainerDefinition> Containers);

    private sealed class GraphvizScopeBuilder
    {
        private readonly bool _compact;
        private readonly Func<string, bool> _isExpanded;
        private readonly Dictionary<string, GraphvizNodeDefinition> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphvizEdgeDefinition> _edges = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphvizContainerDefinition> _containers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _scopeBoundaryPorts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _scopeLocalNodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _localSelectionNamesByNodeId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GraphvizNodeDefinition> _instanceNodesByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _expandedPortNodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _expandedLocalNodes = new(StringComparer.OrdinalIgnoreCase);
        private int _edgeSequence;

        public GraphvizScopeBuilder(bool compact, Func<string, bool> isExpanded)
        {
            _compact = compact;
            _isExpanded = isExpanded;
        }

        public void AddScopeBoundary(string scopeTitle, IReadOnlyList<HierarchyScopePortViewModel> ports)
        {
            foreach (HierarchyScopePortViewModel port in ports)
            {
                GraphvizNodeKind kind = port.IsInput ? GraphvizNodeKind.InputPort : GraphvizNodeKind.OutputPort;
                string id = $"scope_{(port.IsInput ? "in" : "out")}_{SanitizeId(port.Name)}";
                AddNode(new GraphvizNodeDefinition(id, port.Name, scopeTitle, kind, port.WidthLabel, port.Name));
                _scopeBoundaryPorts[port.Name] = id;
            }
        }

        public void AddLocal(HierarchyScopeLocalSignalViewModel local)
        {
            string id = $"local_{SanitizeId(local.Name)}";
            AddNode(new GraphvizNodeDefinition(id, local.Name, "local", GraphvizNodeKind.Local, local.WidthLabel, local.ResolvedSignalName));
            _scopeLocalNodes[local.Name] = id;
            _localSelectionNamesByNodeId[id] = local.ResolvedSignalName ?? local.Name;
        }

        public void AddInstance(HierarchyScopeInstanceViewModel instance)
        {
            bool expanded = _isExpanded(instance.HierarchyPath) && instance.ChildInstances.Count > 0;
            if (!expanded)
            {
                string id = InstanceNodeId(instance);
                GraphvizNodeDefinition node = new(
                    id,
                    instance.InstanceName,
                    instance.ModuleName,
                    GraphvizNodeKind.Module,
                    instance.ScopeBadgeText,
                    null,
                    instance.HierarchyPath,
                    instance.ChildInstances.Count > 0,
                    false,
                    BuildPortLabels(instance.PortConnections, input: true),
                    BuildPortLabels(instance.PortConnections, input: false));
                AddNode(node);
                _instanceNodesByPath[instance.HierarchyPath] = node;
                return;
            }

            AddExpandedInstanceBoundary(instance);
            foreach (HierarchyScopeInstanceViewModel child in instance.ChildInstances)
            {
                string id = $"{SanitizeId(instance.HierarchyPath)}_{InstanceNodeId(child)}";
                GraphvizNodeDefinition node = new(
                    id,
                    child.InstanceName,
                    child.ModuleName,
                    GraphvizNodeKind.Module,
                    child.ScopeBadgeText,
                    null,
                    child.HierarchyPath,
                    child.ChildInstances.Count > 0,
                    _isExpanded(child.HierarchyPath),
                    BuildPortLabels(child.PortConnections, input: true),
                    BuildPortLabels(child.PortConnections, input: false),
                    instance.HierarchyPath);
                AddNode(node);
                _instanceNodesByPath[child.HierarchyPath] = node;
            }
        }

        public void AddScopeConnections(
            IReadOnlyList<HierarchyScopeInstanceViewModel> instances,
            IReadOnlyList<HierarchyScopePortViewModel> ports,
            IReadOnlyList<HierarchyScopeLocalSignalViewModel> locals)
        {
            foreach (HierarchyScopeInstanceViewModel instance in instances)
            {
                bool expanded = _isExpanded(instance.HierarchyPath) && instance.ChildInstances.Count > 0;
                AddConnectionsForInstance(instance, _scopeBoundaryPorts, _scopeLocalNodes, expanded);
                if (expanded)
                {
                    AddExpandedInternalConnections(instance);
                }
            }
        }

        public GraphvizScopeGraph Build()
        {
            StringBuilder builder = new();
            builder.AppendLine("digraph G {");
            builder.AppendLine("  graph [rankdir=LR, splines=ortho, nodesep=0.85, ranksep=1.95, margin=0.02, outputorder=edgesfirst, compound=true];");
            builder.AppendLine("  node [fontname=\"monospace\", fontsize=10, margin=0.07];");
            builder.AppendLine("  edge [arrowsize=0.45, penwidth=1.2];");
            foreach (GraphvizContainerDefinition container in _containers.Values)
            {
                GraphvizNodeDefinition[] containerNodes = _nodes.Values
                    .Where(node => string.Equals(node.ContainerPath, container.ScopePath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                builder
                    .Append("  subgraph cluster_")
                    .Append(SanitizeId(container.ScopePath))
                    .AppendLine(" {");
                builder.AppendLine("    label=\"\";");
                builder.AppendLine("    color=\"#2a3241\";");
                builder.AppendLine("    margin=24;");
                foreach (GraphvizNodeDefinition node in containerNodes)
                {
                    AppendNode(builder, node, indent: "    ");
                }

                AppendRank(builder, containerNodes.Where(static node => node.Kind == GraphvizNodeKind.InputPort).Select(static node => node.Id));
                AppendRank(builder, containerNodes.Where(static node => node.Kind == GraphvizNodeKind.OutputPort).Select(static node => node.Id));
                builder.AppendLine("  }");
            }

            foreach (GraphvizNodeDefinition node in _nodes.Values.Where(static node => string.IsNullOrWhiteSpace(node.ContainerPath)))
            {
                AppendNode(builder, node, indent: "  ");
            }

            foreach (GraphvizEdgeDefinition edge in _edges.Values)
            {
                builder
                    .Append("  ")
                    .Append(edge.TailRef)
                    .Append(" -> ")
                    .Append(edge.HeadRef)
                    .Append(" [label=\"")
                    .Append(edge.Id)
                    .AppendLine("\", fontsize=1, fontcolor=\"#0e1116\"];");
            }

            AppendRank(builder, _nodes.Values.Where(static node => node.Kind == GraphvizNodeKind.InputPort && string.IsNullOrWhiteSpace(node.ContainerPath)).Select(static node => node.Id));
            AppendRank(builder, _nodes.Values.Where(static node => node.Kind == GraphvizNodeKind.OutputPort && string.IsNullOrWhiteSpace(node.ContainerPath)).Select(static node => node.Id));
            builder.AppendLine("}");

            string dot = builder.ToString();
            string key = dot + string.Join('|', _nodes.Values.Select(static node => $"{node.Id}:{node.Title}:{node.Subtitle}:{node.WidthLabel}:{node.Expanded}"));
            return new GraphvizScopeGraph(dot, key, _nodes, _edges, _containers.Values.ToArray());
        }

        private static void AppendNode(StringBuilder builder, GraphvizNodeDefinition node, string indent)
        {
            builder.Append(indent).Append(node.Id);
            if (node.Kind == GraphvizNodeKind.Module)
            {
                (double moduleWidth, double moduleHeight) = NodeSize(node);
                builder
                    .Append(" [shape=record, label=\"")
                    .Append(BuildRecordLabel(node))
                    .Append("\", width=\"")
                    .Append(moduleWidth.ToString("0.###", DotCulture))
                    .Append("\", height=\"")
                    .Append(moduleHeight.ToString("0.###", DotCulture))
                    .AppendLine("\"];");
                return;
            }

            (double width, double height) = NodeSize(node);
            builder
                .Append(" [shape=box, fixedsize=true, label=\"\", width=\"")
                .Append(width.ToString("0.###", DotCulture))
                .Append("\", height=\"")
                .Append(height.ToString("0.###", DotCulture))
                .AppendLine("\"];");
        }

        private static string BuildRecordLabel(GraphvizNodeDefinition node)
        {
            string left = node.InputLabels.Count == 0
                ? " "
                : string.Join("|", node.InputLabels.Select(label => $"<{PortId(ExtractPortName(label), input: true)}> {EscapeRecordText(label)}"));
            string middle = $"{EscapeRecordText(node.Title)}\\n{EscapeRecordText(node.Subtitle ?? string.Empty)}";
            string right = node.OutputLabels.Count == 0
                ? " "
                : string.Join("|", node.OutputLabels.Select(label => $"<{PortId(ExtractPortName(label), input: false)}> {EscapeRecordText(label)}"));
            return $"{{{{{left}}}|{{{middle}}}|{{{right}}}}}";
        }

        private static string ExtractPortName(string label)
        {
            int widthStart = label.IndexOf(" [", StringComparison.Ordinal);
            return widthStart > 0 ? label[..widthStart] : label;
        }

        private static string EscapeRecordText(string value) =>
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("{", "\\{", StringComparison.Ordinal)
                .Replace("}", "\\}", StringComparison.Ordinal)
                .Replace("|", "\\|", StringComparison.Ordinal)
                .Replace("<", "\\<", StringComparison.Ordinal)
                .Replace(">", "\\>", StringComparison.Ordinal);

        private void AddExpandedInstanceBoundary(HierarchyScopeInstanceViewModel instance)
        {
            Dictionary<string, string> portNodes = new(StringComparer.OrdinalIgnoreCase);
            foreach (HierarchyScopePortViewModel port in instance.Ports)
            {
                string id = $"{SanitizeId(instance.HierarchyPath)}_{(port.IsInput ? "in" : "out")}_{SanitizeId(port.Name)}";
                AddNode(new GraphvizNodeDefinition(
                    id,
                    port.Name,
                    instance.InstanceName,
                    port.IsInput ? GraphvizNodeKind.InputPort : GraphvizNodeKind.OutputPort,
                    port.WidthLabel,
                    port.Name,
                    ContainerPath: instance.HierarchyPath));
                portNodes[port.Name] = id;
            }

            _expandedPortNodes[instance.HierarchyPath] = portNodes;
            _containers[instance.HierarchyPath] = new GraphvizContainerDefinition(instance.HierarchyPath, instance.InstanceName, instance.ModuleName);

            Dictionary<string, string> localNodes = new(StringComparer.OrdinalIgnoreCase);
            foreach (HierarchyScopeLocalSignalViewModel local in instance.LocalSignals)
            {
                string id = $"{SanitizeId(instance.HierarchyPath)}_local_{SanitizeId(local.Name)}";
                AddNode(new GraphvizNodeDefinition(
                    id,
                    local.Name,
                    instance.InstanceName,
                    GraphvizNodeKind.Local,
                    local.WidthLabel,
                    local.ResolvedSignalName,
                    ContainerPath: instance.HierarchyPath));
                localNodes[local.Name] = id;
                _localSelectionNamesByNodeId[id] = local.ResolvedSignalName ?? local.Name;
            }

            _expandedLocalNodes[instance.HierarchyPath] = localNodes;
        }

        private void AddExpandedInternalConnections(HierarchyScopeInstanceViewModel instance)
        {
            Dictionary<string, string> boundary = _expandedPortNodes[instance.HierarchyPath];
            Dictionary<string, string> locals = _expandedLocalNodes[instance.HierarchyPath];
            foreach (HierarchyScopeInstanceViewModel child in instance.ChildInstances)
            {
                AddConnectionsForInstance(child, boundary, locals, expandedTarget: false);
            }
        }

        private void AddConnectionsForInstance(
            HierarchyScopeInstanceViewModel instance,
            IReadOnlyDictionary<string, string> boundaryPorts,
            IReadOnlyDictionary<string, string> localNodes,
            bool expandedTarget)
        {
            Dictionary<string, string>? expandedPorts = null;
            string targetNode = string.Empty;
            if (expandedTarget)
            {
                _expandedPortNodes.TryGetValue(instance.HierarchyPath, out expandedPorts);
            }
            else
            {
                targetNode = _instanceNodesByPath.TryGetValue(instance.HierarchyPath, out GraphvizNodeDefinition? node)
                    ? node.Id
                    : InstanceNodeId(instance);
            }

            foreach (HierarchyScopeInstancePortConnectionViewModel connection in instance.PortConnections)
            {
                string? externalNode = ResolveSignalNode(connection.SignalName, boundaryPorts, localNodes);
                if (externalNode is null)
                {
                    continue;
                }

                string instanceEndpoint = expandedTarget && expandedPorts is not null
                    ? expandedPorts.TryGetValue(connection.PortName, out string? expandedPortNode) ? expandedPortNode : targetNode
                    : targetNode;
                if (string.IsNullOrWhiteSpace(instanceEndpoint))
                {
                    continue;
                }

                bool connectsLocalSignal = localNodes.TryGetValue(connection.SignalName, out string? localNodeId)
                    && string.Equals(localNodeId, externalNode, StringComparison.OrdinalIgnoreCase);
                string? selectionSignalName = connectsLocalSignal && _localSelectionNamesByNodeId.TryGetValue(externalNode, out string? localSelectionName)
                    ? localSelectionName
                    : connection.SignalName;

                if (connection.IsInput)
                {
                    string headRef = expandedTarget
                        ? $"{instanceEndpoint}:w"
                        : $"{instanceEndpoint}:{PortId(connection.PortName, input: true)}:w";
                    AddEdge(
                        externalNode,
                        instanceEndpoint,
                        $"{externalNode}:e",
                        headRef,
                        connection.SignalName,
                        selectionSignalName,
                        connection.Width,
                        GraphvizEdgeKind.Input,
                        suppressLabel: connectsLocalSignal,
                        tailPortName: null,
                        headPortName: expandedTarget ? null : connection.PortName);
                }
                else
                {
                    string tailRef = expandedTarget
                        ? $"{instanceEndpoint}:e"
                        : $"{instanceEndpoint}:{PortId(connection.PortName, input: false)}:e";
                    AddEdge(
                        instanceEndpoint,
                        externalNode,
                        tailRef,
                        $"{externalNode}:w",
                        connection.SignalName,
                        selectionSignalName,
                        connection.Width,
                        GraphvizEdgeKind.Output,
                        suppressLabel: connectsLocalSignal,
                        tailPortName: expandedTarget ? null : connection.PortName,
                        headPortName: null);
                }
            }
        }

        private string? ResolveSignalNode(
            string signalName,
            IReadOnlyDictionary<string, string> boundaryPorts,
            IReadOnlyDictionary<string, string> localNodes)
        {
            if (boundaryPorts.TryGetValue(signalName, out string? boundaryNode))
            {
                return boundaryNode;
            }

            if (localNodes.TryGetValue(signalName, out string? localNode))
            {
                return localNode;
            }

            string id = $"anon_{SanitizeId(signalName)}";
            AddNode(new GraphvizNodeDefinition(id, signalName, "net", GraphvizNodeKind.Local, null, signalName));
            return id;
        }

        private void AddNode(GraphvizNodeDefinition node) => _nodes.TryAdd(node.Id, node);

        private void AddEdge(
            string tail,
            string head,
            string tailRef,
            string headRef,
            string signalName,
            string? selectionSignalName,
            int width,
            GraphvizEdgeKind kind,
            bool suppressLabel,
            string? tailPortName,
            string? headPortName)
        {
            if (tail == head)
            {
                return;
            }

            string id = $"e{_edgeSequence++}";
            _edges[id] = new GraphvizEdgeDefinition(
                id,
                tail,
                head,
                tailRef,
                headRef,
                signalName,
                selectionSignalName,
                width,
                kind,
                suppressLabel,
                tailPortName,
                headPortName);
        }

        private static (double Width, double Height) NodeSize(GraphvizNodeDefinition node) =>
            node.Kind == GraphvizNodeKind.Module
                ? (3.10, Math.Max(1.55, 1.05 + Math.Max(node.InputLabels.Count, node.OutputLabels.Count) * 0.42))
                : node.Kind is GraphvizNodeKind.InputPort or GraphvizNodeKind.OutputPort
                    ? (0.20, 0.20)
                    : (1.25, 0.30);

        private static string PortId(string portName, bool input) =>
            $"{(input ? "in" : "out")}_{SanitizeId(portName)}";

        private static IReadOnlyList<string> BuildPortLabels(
            IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> connections,
            bool input)
        {
            return connections
                .Where(connection => connection.IsInput == input)
                .Select(static connection => connection.Width <= 1
                    ? connection.PortName
                    : $"{connection.PortName} [{connection.Width}b]")
                .ToArray();
        }

        private static void AppendRank(StringBuilder builder, IEnumerable<string> ids)
        {
            string[] nodes = ids.ToArray();
            if (nodes.Length == 0)
            {
                return;
            }

            builder.Append("  { rank=same; ");
            foreach (string id in nodes)
            {
                builder.Append(id).Append("; ");
            }

            builder.AppendLine("}");
        }

        private static string InstanceNodeId(HierarchyScopeInstanceViewModel instance) => $"inst_{SanitizeId(instance.HierarchyPath)}";

        public static string EdgeKey(string tail, string head) => $"{tail}->{head}";

        private static string SanitizeId(string value)
        {
            StringBuilder builder = new(value.Length + 8);
            foreach (char c in value)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.Length == 0 ? "node" : builder.ToString();
        }
    }

    private sealed record GraphvizPlainNode(string Id, Point Position, double Width, double Height);

    private sealed record GraphvizPlainEdge(string Tail, string Head, string Label, IReadOnlyList<Point> Points);

    private sealed record GraphvizPlainDiagram(double Width, double Height, IReadOnlyList<GraphvizPlainNode> Nodes, IReadOnlyList<GraphvizPlainEdge> Edges)
    {
        public static GraphvizPlainDiagram Parse(string plain)
        {
            double width = 1;
            double height = 1;
            List<GraphvizPlainNode> nodes = [];
            List<GraphvizPlainEdge> edges = [];
            using StringReader reader = new(plain);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (tokens[0] == "graph" && tokens.Length >= 4)
                {
                    width = double.Parse(tokens[2], DotCulture);
                    height = double.Parse(tokens[3], DotCulture);
                    continue;
                }

                if (tokens[0] == "node" && tokens.Length >= 6)
                {
                    nodes.Add(new GraphvizPlainNode(
                        tokens[1],
                        new Point(double.Parse(tokens[2], DotCulture), double.Parse(tokens[3], DotCulture)),
                        double.Parse(tokens[4], DotCulture),
                        double.Parse(tokens[5], DotCulture)));
                    continue;
                }

                if (tokens[0] == "edge" && tokens.Length >= 5 && int.TryParse(tokens[3], NumberStyles.Integer, DotCulture, out int pointCount))
                {
                    Point[] points = new Point[pointCount];
                    for (int index = 0; index < pointCount; index++)
                    {
                        points[index] = new Point(
                            double.Parse(tokens[4 + index * 2], DotCulture),
                            double.Parse(tokens[5 + index * 2], DotCulture));
                    }

                    int labelIndex = 4 + pointCount * 2;
                    string label = labelIndex < tokens.Length ? tokens[labelIndex] : string.Empty;
                    edges.Add(new GraphvizPlainEdge(tokens[1], tokens[2], label, points));
                }
            }

            return new GraphvizPlainDiagram(width, height, nodes, edges);
        }
    }
}
