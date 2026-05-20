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
            Point elbow1 = new(laneX, request.Source.Y);
            Point elbow2 = new(laneX, request.Target.Y);
            routes.Add(new SchematicConnectionRoute(request.Id, [request.Source, elbow1, elbow2, request.Target]));
        }

        return routes;
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
            routes.Add(new SchematicConnectionRoute(request.Id, [request.Source, elbow1, elbow2, request.Target]));
        }

        return routes;
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
}

public sealed record SchematicConnectionRoutingInput(
    SchematicScopePanelLayout Layout,
    bool CompactLayout,
    IReadOnlyList<SchematicConnectionRouteRequest> Requests);

public sealed record SchematicConnectionRouteRequest(
    string Id,
    Point Source,
    Point Target,
    bool SourceFromLocalSignal,
    bool TargetIsInput);

public sealed record SchematicConnectionRoute(
    string Id,
    IReadOnlyList<Point> Points);
