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

        Dictionary<string, double> inputLanes = AssignLanes(inputRoutes, leftLaneStart, leftLaneEnd);
        Dictionary<string, double> outputLanes = AssignLanes(
            outputRoutes,
            Math.Max(rightLaneStart, leftLaneEnd + laneMargin),
            Math.Max(rightLaneStart, rightLaneEnd));

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicConnectionRouteRequest request in input.Requests)
        {
            double laneX = request.TargetIsInput
                ? inputLanes[request.Id]
                : outputLanes[request.Id];
            if (request.TargetIsInput)
            {
                Point elbow1 = new(laneX, request.Source.Y);
                Point elbow2 = new(laneX, request.Target.Y);
                IReadOnlyList<Point> points = [request.Source, elbow1, elbow2, request.Target];
                routes.Add(new SchematicConnectionRoute(
                    request.Id,
                    points,
                    BuildLabelBounds(new Point(laneX, (request.Source.Y + request.Target.Y) / 2), request.LabelWidth),
                    new Point(laneX, (request.Source.Y + request.Target.Y) / 2)));
                continue;
            }

            double bridgeY = request.SourceFromLocalSignal
                ? Math.Max(
                    input.Layout.ChildNodeRects.Count == 0 ? request.Source.Y : input.Layout.ChildNodeRects.Max(static rect => rect.Bottom) + (input.CompactLayout ? 16 : 22),
                    request.Source.Y)
                : Math.Min(input.Layout.CurrentNodeRect.Y, input.Layout.ChildNodeRects.Count == 0 ? input.Layout.CurrentNodeRect.Y : input.Layout.ChildNodeRects.Min(static rect => rect.Y)) - (input.CompactLayout ? 18 : 24);
            double corridorX = Math.Min(laneX - laneMargin, input.Layout.CurrentNodeRect.Right + input.Layout.RouteCorridorWidth * 0.38);
            IReadOnlyList<Point> outputPoints =
            [
                    request.Source,
                    new Point(corridorX, request.Source.Y),
                    new Point(corridorX, bridgeY),
                    new Point(laneX, bridgeY),
                    new Point(laneX, request.Target.Y),
                    request.Target
            ];
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                outputPoints,
                BuildLabelBounds(new Point(laneX, bridgeY), request.LabelWidth),
                new Point(laneX, bridgeY)));
        }

        return PlaceLabels(routes, input.Layout.PanelRect, input.CompactLayout);
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

        Dictionary<string, double> currentLanes = AssignLanes(currentRoutes, upperLaneStart, Math.Max(upperLaneStart, upperLaneEnd));
        Dictionary<string, double> localLanes = AssignLanes(localRoutes, lowerLaneStart, Math.Max(lowerLaneStart, lowerLaneEnd));

        List<SchematicConnectionRoute> routes = [];
        foreach (SchematicConnectionRouteRequest request in input.Requests)
        {
            double laneY = request.SourceFromLocalSignal
                ? localLanes[request.Id]
                : currentLanes[request.Id];
            Point elbow1 = new(request.Source.X, laneY);
            Point elbow2 = new(request.Target.X, laneY);
            IReadOnlyList<Point> points = [request.Source, elbow1, elbow2, request.Target];
            routes.Add(new SchematicConnectionRoute(
                request.Id,
                points,
                BuildLabelBounds(new Point((request.Source.X + request.Target.X) / 2, laneY), request.LabelWidth),
                new Point((request.Source.X + request.Target.X) / 2, laneY)));
        }

        return PlaceLabels(routes, input.Layout.PanelRect, input.CompactLayout);
    }

    private static Dictionary<string, double> AssignLanes(
        IReadOnlyList<SchematicConnectionRouteRequest> requests,
        double start,
        double end)
    {
        Dictionary<string, double> lanes = new(StringComparer.OrdinalIgnoreCase);
        if (requests.Count == 0)
        {
            return lanes;
        }

        if (requests.Count == 1 || Math.Abs(end - start) < 1)
        {
            lanes[requests[0].Id] = (start + end) / 2;
            for (int index = 1; index < requests.Count; index++)
            {
                lanes[requests[index].Id] = lanes[requests[0].Id];
            }

            return lanes;
        }

        double step = (end - start) / (requests.Count + 1);
        for (int index = 0; index < requests.Count; index++)
        {
            lanes[requests[index].Id] = start + step * (index + 1);
        }

        return lanes;
    }

    private static Rect BuildLabelBounds(Point anchor, int width)
    {
        double labelWidth = width <= 1 ? 30 : Math.Clamp(24 + width * 2.4, 36, 72);
        return new Rect(anchor.X - labelWidth / 2, anchor.Y - 9, labelWidth, 18);
    }

    private static IReadOnlyList<SchematicConnectionRoute> PlaceLabels(
        IReadOnlyList<SchematicConnectionRoute> routes,
        Rect bounds,
        bool compactLayout)
    {
        if (routes.Count <= 1)
        {
            return routes;
        }

        double margin = compactLayout ? 10 : 14;
        double verticalStep = compactLayout ? 20 : 22;
        double minX = bounds.X + margin;
        double maxX = Math.Max(minX, bounds.Right - margin);
        double minY = bounds.Y + margin;
        double maxY = Math.Max(minY, bounds.Bottom - margin);
        List<Rect> placed = [];
        Dictionary<string, Rect> labels = new(StringComparer.OrdinalIgnoreCase);

        foreach (SchematicConnectionRoute route in routes.OrderBy(static route => route.LabelBounds.Y).ThenBy(static route => route.LabelBounds.X))
        {
            Rect label = ClampLabel(route.LabelBounds, minX, maxX, minY, maxY);
            int guard = 0;
            while (placed.Any(candidate => candidate.Inflate(2).Intersects(label)) && guard < 24)
            {
                double nextY = label.Y + verticalStep;
                if (nextY + label.Height > maxY)
                {
                    nextY = minY + guard * 3;
                }

                label = ClampLabel(new Rect(label.X, nextY, label.Width, label.Height), minX, maxX, minY, maxY);
                guard++;
            }

            placed.Add(label);
            labels[route.Id] = label;
        }

        return routes
            .Select(route => route with { LabelBounds = labels.TryGetValue(route.Id, out Rect label) ? label : route.LabelBounds })
            .ToList();
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
    IReadOnlyList<SchematicConnectionRouteRequest> Requests);

public sealed record SchematicConnectionRouteRequest(
    string Id,
    string SignalName,
    string? SelectionSignalName,
    int LabelWidth,
    Point Source,
    Point Target,
    bool SourceFromLocalSignal,
    bool TargetIsInput);

public sealed record SchematicConnectionRoute(
    string Id,
    IReadOnlyList<Point> Points,
    Rect LabelBounds,
    Point LabelAnchor);
