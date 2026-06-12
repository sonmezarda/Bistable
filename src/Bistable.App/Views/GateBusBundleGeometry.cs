using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Projects;

namespace Bistable.App.Views;

public sealed record GateBusDisplayOptions(
    GateBusVisualizationMode Mode,
    double TrunkMaxZoom)
{
    public static GateBusDisplayOptions Default { get; } =
        new(GateBusVisualizationMode.Automatic, 0.9);

    public bool UsesTrunks(double zoom) =>
        Mode == GateBusVisualizationMode.Bundled
        || (Mode == GateBusVisualizationMode.Automatic && zoom < TrunkMaxZoom);

    public GateBusDisplayOptions Normalize() =>
        this with
        {
            TrunkMaxZoom = double.IsFinite(TrunkMaxZoom)
                ? Math.Clamp(TrunkMaxZoom, 0.05, 8.0)
                : Default.TrunkMaxZoom,
        };
}

internal sealed record GateBusBundleGeometry(
    string BundleId,
    int RepresentativeNetId,
    IReadOnlyList<GateBusGeometrySegment> TrunkSegments,
    IReadOnlyList<GateBusGeometrySegment> FanSegments)
{
    public IEnumerable<GateBusGeometrySegment> AllSegments =>
        TrunkSegments.Concat(FanSegments);
}

internal readonly record struct GateBusGeometrySegment(
    Point Start,
    Point End,
    int? NetId = null);

internal static class GateBusBundleGeometryBuilder
{
    private const double CollectorLead = 12;
    private const double Epsilon = 0.001;

    public static IReadOnlyDictionary<string, GateBusBundleGeometry> Build(
        ElkGraph graph,
        IReadOnlyDictionary<string, GateBusBundle> bundles,
        GateElkGeometry geometry)
    {
        if (bundles.Count == 0 || graph.Edges is not { Count: > 0 })
        {
            return new Dictionary<string, GateBusBundleGeometry>(StringComparer.Ordinal);
        }

        IReadOnlyDictionary<string, ElkEdge> edges = graph.Edges
            .ToDictionary(static edge => edge.Id, StringComparer.Ordinal);
        Dictionary<string, GateBusBundleGeometry> result = new(StringComparer.Ordinal);
        foreach (GateBusBundle bundle in bundles.Values)
        {
            if (TryBuild(bundle, edges, geometry) is { } bundleGeometry)
            {
                result[bundle.Id] = bundleGeometry;
            }
        }
        return result;
    }

    private static GateBusBundleGeometry? TryBuild(
        GateBusBundle bundle,
        IReadOnlyDictionary<string, ElkEdge> edges,
        GateElkGeometry geometry)
    {
        List<MemberGeometry> members = [];
        foreach (GateBusBundleMember member in bundle.Members)
        {
            if (!edges.TryGetValue(member.EdgeId, out ElkEdge? edge)
                || !geometry.TryGetPortPosition(member.SourcePortId, out Point source)
                || !geometry.TryGetPortPosition(member.TargetPortId, out Point target))
            {
                continue;
            }

            IReadOnlyList<GateBusGeometrySegment> segments = geometry.GetEdgeSegments(edge);
            if (segments.Count == 0)
            {
                continue;
            }
            members.Add(new MemberGeometry(member, source, target, segments));
        }

        if (members.Count < 2 || members.Count != bundle.Members.Count)
        {
            return null;
        }

        MemberGeometry representative = members[members.Count / 2];
        IReadOnlyList<GateBusGeometrySegment> trunk = representative.Segments;
        Point sourceAnchor = ClosestSegmentEndpoint(trunk, representative.Source);
        Point targetAnchor = ClosestSegmentEndpoint(trunk, representative.Target);
        GateBusGeometrySegment? sourceIncident = FindIncidentSegment(trunk, sourceAnchor);
        GateBusGeometrySegment? targetIncident = FindIncidentSegment(trunk, targetAnchor);

        List<GateBusGeometrySegment> fans = [];
        fans.AddRange(BuildCollector(
            members.Select(static member => (member.Source, member.Member.NetId)),
            sourceAnchor,
            sourceIncident));
        fans.AddRange(BuildCollector(
            members.Select(static member => (member.Target, member.Member.NetId)),
            targetAnchor,
            targetIncident));

        return new GateBusBundleGeometry(
            bundle.Id,
            representative.Member.NetId,
            trunk,
            Deduplicate(fans));
    }

    private static IReadOnlyList<GateBusGeometrySegment> BuildCollector(
        IEnumerable<(Point Endpoint, int NetId)> memberEndpoints,
        Point anchor,
        GateBusGeometrySegment? incident)
    {
        (Point Endpoint, int NetId)[] endpoints = memberEndpoints.ToArray();
        if (endpoints.Length < 2)
        {
            return [];
        }

        Point inward = incident is { } segment
            ? OtherEndpoint(segment, anchor)
            : anchor;
        bool horizontal = Math.Abs(inward.X - anchor.X) >= Math.Abs(inward.Y - anchor.Y);
        List<GateBusGeometrySegment> result = [];

        if (horizontal)
        {
            double direction = Math.Sign(inward.X - anchor.X);
            if (direction == 0) direction = 1;
            double collectorX = anchor.X + direction * CollectorLead;
            foreach ((Point endpoint, int netId) in endpoints)
            {
                if (!PointsEqual(endpoint, anchor))
                {
                    AddIfVisible(result, endpoint, new Point(collectorX, endpoint.Y), netId);
                }
            }
            AddIfVisible(
                result,
                new Point(collectorX, endpoints.Min(static item => item.Endpoint.Y)),
                new Point(collectorX, endpoints.Max(static item => item.Endpoint.Y)));
        }
        else
        {
            double direction = Math.Sign(inward.Y - anchor.Y);
            if (direction == 0) direction = 1;
            double collectorY = anchor.Y + direction * CollectorLead;
            foreach ((Point endpoint, int netId) in endpoints)
            {
                if (!PointsEqual(endpoint, anchor))
                {
                    AddIfVisible(result, endpoint, new Point(endpoint.X, collectorY), netId);
                }
            }
            AddIfVisible(
                result,
                new Point(endpoints.Min(static item => item.Endpoint.X), collectorY),
                new Point(endpoints.Max(static item => item.Endpoint.X), collectorY));
        }

        return result;
    }

    private static Point ClosestSegmentEndpoint(
        IReadOnlyList<GateBusGeometrySegment> segments,
        Point reference)
    {
        Point closest = segments[0].Start;
        double closestDistance = DistanceSquared(closest, reference);
        foreach (GateBusGeometrySegment segment in segments)
        {
            Consider(segment.Start);
            Consider(segment.End);
        }
        return closest;

        void Consider(Point candidate)
        {
            double distance = DistanceSquared(candidate, reference);
            if (distance < closestDistance)
            {
                closest = candidate;
                closestDistance = distance;
            }
        }
    }

    private static GateBusGeometrySegment? FindIncidentSegment(
        IReadOnlyList<GateBusGeometrySegment> segments,
        Point anchor)
    {
        foreach (GateBusGeometrySegment segment in segments)
        {
            if (PointsEqual(segment.Start, anchor) || PointsEqual(segment.End, anchor))
            {
                return segment;
            }
        }
        return null;
    }

    private static Point OtherEndpoint(GateBusGeometrySegment segment, Point anchor) =>
        PointsEqual(segment.Start, anchor) ? segment.End : segment.Start;

    private static IReadOnlyList<GateBusGeometrySegment> Deduplicate(
        IReadOnlyList<GateBusGeometrySegment> segments)
    {
        HashSet<SegmentKey> seen = [];
        List<GateBusGeometrySegment> result = new(segments.Count);
        foreach (GateBusGeometrySegment segment in segments)
        {
            SegmentKey key = SegmentKey.Create(segment.Start, segment.End, segment.NetId);
            if (seen.Add(key))
            {
                result.Add(segment);
            }
        }
        return result;
    }

    private static void AddIfVisible(
        List<GateBusGeometrySegment> segments,
        Point start,
        Point end,
        int? netId = null)
    {
        if (!PointsEqual(start, end))
        {
            segments.Add(new GateBusGeometrySegment(start, end, netId));
        }
    }

    private static bool PointsEqual(Point left, Point right) =>
        Math.Abs(left.X - right.X) < Epsilon
        && Math.Abs(left.Y - right.Y) < Epsilon;

    private static double DistanceSquared(Point left, Point right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private sealed record MemberGeometry(
        GateBusBundleMember Member,
        Point Source,
        Point Target,
        IReadOnlyList<GateBusGeometrySegment> Segments);

    private readonly record struct SegmentKey(
        long X1,
        long Y1,
        long X2,
        long Y2,
        int? NetId)
    {
        public static SegmentKey Create(Point start, Point end, int? netId)
        {
            Point first = start.X < end.X || (Math.Abs(start.X - end.X) < Epsilon && start.Y <= end.Y)
                ? start
                : end;
            Point second = first == start ? end : start;
            return new SegmentKey(
                Quantize(first.X),
                Quantize(first.Y),
                Quantize(second.X),
                Quantize(second.Y),
                netId);
        }

        private static long Quantize(double value) =>
            (long)Math.Round(value * 1000, MidpointRounding.AwayFromZero);
    }
}

internal sealed class GateElkGeometry
{
    private readonly IReadOnlyDictionary<string, string[]> _endpointPaths;
    private readonly IReadOnlyDictionary<string, Point> _nodeOrigins;
    private readonly IReadOnlyDictionary<string, Point> _portPositions;
    private readonly Dictionary<string, IReadOnlyList<GateBusGeometrySegment>> _edgeSegments =
        new(StringComparer.Ordinal);

    private GateElkGeometry(
        IReadOnlyDictionary<string, string[]> endpointPaths,
        IReadOnlyDictionary<string, Point> nodeOrigins,
        IReadOnlyDictionary<string, Point> portPositions)
    {
        _endpointPaths = endpointPaths;
        _nodeOrigins = nodeOrigins;
        _portPositions = portPositions;
    }

    public static GateElkGeometry Build(ElkGraph graph)
    {
        Dictionary<string, string[]> endpointPaths = new(StringComparer.Ordinal);
        Dictionary<string, Point> nodeOrigins = new(StringComparer.Ordinal);
        Dictionary<string, Point> portPositions = new(StringComparer.Ordinal);
        Visit(graph.Children, [], absoluteX: 0, absoluteY: 0);
        GateElkGeometry result = new(endpointPaths, nodeOrigins, portPositions);
        if (graph.Edges is { Count: > 0 })
        {
            foreach (ElkEdge edge in graph.Edges)
            {
                result._edgeSegments[edge.Id] = result.ComputeEdgeSegments(edge);
            }
        }
        return result;

        void Visit(IReadOnlyList<ElkNode> nodes, string[] parentPath, double absoluteX, double absoluteY)
        {
            foreach (ElkNode node in nodes)
            {
                double nodeAbsoluteX = absoluteX + node.X;
                double nodeAbsoluteY = absoluteY + node.Y;
                string[] nodePath = [.. parentPath, node.Id];
                endpointPaths[node.Id] = nodePath;
                nodeOrigins[PathKey(nodePath)] = new Point(nodeAbsoluteX, nodeAbsoluteY);

                if (node.Ports is { Count: > 0 })
                {
                    foreach (ElkPort port in node.Ports)
                    {
                        endpointPaths[port.Id] = nodePath;
                        portPositions[port.Id] =
                            new Point(nodeAbsoluteX + port.X, nodeAbsoluteY + port.Y);
                    }
                }

                if (node.Children is { Count: > 0 })
                {
                    Visit(node.Children, nodePath, nodeAbsoluteX, nodeAbsoluteY);
                }
            }
        }
    }

    public bool TryGetPortPosition(string portId, out Point position) =>
        _portPositions.TryGetValue(portId, out position);

    public Point ResolveEdgeOffset(ElkEdge edge)
    {
        string[]? commonPath = null;
        foreach (string endpoint in edge.Sources.Concat(edge.Targets))
        {
            if (!_endpointPaths.TryGetValue(endpoint, out string[]? endpointPath))
            {
                continue;
            }

            commonPath = commonPath is null
                ? endpointPath
                : CommonPrefix(commonPath, endpointPath);
            if (commonPath.Length == 0)
            {
                return default;
            }
        }

        return commonPath is { Length: > 0 }
            && _nodeOrigins.TryGetValue(PathKey(commonPath), out Point origin)
                ? origin
                : default;
    }

    public IReadOnlyList<GateBusGeometrySegment> GetEdgeSegments(ElkEdge edge)
        => _edgeSegments.TryGetValue(edge.Id, out IReadOnlyList<GateBusGeometrySegment>? segments)
            ? segments
            : [];

    private IReadOnlyList<GateBusGeometrySegment> ComputeEdgeSegments(ElkEdge edge)
    {
        if (edge.Sections is not { Count: > 0 })
        {
            return [];
        }

        Point offset = ResolveEdgeOffset(edge);
        List<GateBusGeometrySegment> segments = [];
        foreach (ElkEdgeSection section in edge.Sections)
        {
            Point previous = OffsetPoint(section.StartPoint, offset);
            if (section.BendPoints is { Count: > 0 })
            {
                foreach (ElkPoint bend in section.BendPoints)
                {
                    Point current = OffsetPoint(bend, offset);
                    AddSegment(segments, previous, current);
                    previous = current;
                }
            }
            AddSegment(segments, previous, OffsetPoint(section.EndPoint, offset));
        }
        return segments;
    }

    private static void AddSegment(List<GateBusGeometrySegment> segments, Point start, Point end)
    {
        if (start != end)
        {
            segments.Add(new GateBusGeometrySegment(start, end));
        }
    }

    private static string[] CommonPrefix(string[] left, string[] right)
    {
        int count = Math.Min(left.Length, right.Length);
        int i = 0;
        while (i < count && string.Equals(left[i], right[i], StringComparison.Ordinal))
        {
            i++;
        }
        return left[..i];
    }

    private static string PathKey(IReadOnlyList<string> path) => string.Join('\u001f', path);

    private static Point OffsetPoint(ElkPoint point, Point offset) =>
        new(point.X + offset.X, point.Y + offset.Y);
}
