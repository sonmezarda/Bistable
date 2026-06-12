using Avalonia;
using Bistable.App.Services.Routing.Elk;

namespace Bistable.App.Views;

internal enum GatePinLabelPlacementKind
{
    InsideAbove,
    InsideBelow,
    OutsideAbove,
    OutsideBelow,
}

internal sealed record GatePinLabelPlacementRequest(
    string Id,
    string NodeId,
    Point PortCenter,
    bool IsWestSide,
    Size TextSize,
    bool IsPriority);

internal sealed record GatePinLabelObstacle(
    Rect Bounds,
    string? AllowedOwnerNodeId = null,
    string? AllowedRequestId = null);

internal sealed record GatePinLabelPlacement(
    string Id,
    Point TextOrigin,
    Rect TextBounds,
    GatePinLabelPlacementKind Kind);

internal static class GatePinLabelPlacementEngine
{
    private const double HorizontalOffset = 6;
    private const double VerticalOffset = 3;
    private const double BackgroundPaddingX = 2;
    private const double BackgroundPaddingY = 1;
    private const double ScreenGap = 2;

    public static IReadOnlyDictionary<string, GatePinLabelPlacement> Place(
        IReadOnlyList<GatePinLabelPlacementRequest> requests,
        IReadOnlyList<GatePinLabelObstacle> obstacles,
        Rect worldViewport,
        double zoom)
    {
        if (requests.Count == 0 || zoom <= 0 || worldViewport.Width <= 0 || worldViewport.Height <= 0)
        {
            return new Dictionary<string, GatePinLabelPlacement>(StringComparer.Ordinal);
        }

        Rect screenViewport = new(0, 0, worldViewport.Width * zoom, worldViewport.Height * zoom);
        ScreenOccupancyIndex occupancy = new(cellSize: 64);
        foreach (GatePinLabelObstacle obstacle in obstacles)
        {
            Rect screenBounds = ToScreen(obstacle.Bounds, worldViewport, zoom);
            if (screenBounds.Intersects(screenViewport))
            {
                occupancy.Add(new OccupiedRect(
                    screenBounds,
                    obstacle.AllowedOwnerNodeId,
                    obstacle.AllowedRequestId));
            }
        }

        Dictionary<string, GatePinLabelPlacement> result = new(StringComparer.Ordinal);
        IEnumerable<GatePinLabelPlacementRequest> ordered = requests
            .OrderByDescending(static request => request.IsPriority)
            .ThenBy(static request => request.NodeId, StringComparer.Ordinal)
            .ThenBy(static request => request.PortCenter.Y)
            .ThenBy(static request => request.Id, StringComparer.Ordinal);

        foreach (GatePinLabelPlacementRequest request in ordered)
        {
            Candidate[] candidates = BuildCandidates(request);
            Candidate? selected = candidates
                .Where(candidate => candidate.ScreenBounds.Intersects(screenViewport))
                .FirstOrDefault(candidate =>
                    occupancy.CountIntersections(candidate.ScreenBounds, request) == 0);

            if (selected is null && request.IsPriority)
            {
                selected = candidates
                    .Where(candidate => candidate.ScreenBounds.Intersects(screenViewport))
                    .OrderBy(candidate =>
                        occupancy.CountIntersections(candidate.ScreenBounds, request))
                    .ThenBy(static candidate => candidate.Order)
                    .FirstOrDefault();
            }

            if (selected is null)
            {
                continue;
            }

            occupancy.Add(new OccupiedRect(
                selected.ScreenBounds,
                AllowedOwnerNodeId: null,
                AllowedRequestId: null));
            result[request.Id] = new GatePinLabelPlacement(
                request.Id,
                selected.WorldOrigin,
                selected.WorldBounds,
                selected.Kind);
        }

        return result;

        Candidate[] BuildCandidates(GatePinLabelPlacementRequest request)
        {
            double width = request.TextSize.Width;
            double height = request.TextSize.Height;
            Point centre = request.PortCenter;
            double insideX = request.IsWestSide
                ? centre.X + HorizontalOffset
                : centre.X - HorizontalOffset - width;
            double outsideX = request.IsWestSide
                ? centre.X - HorizontalOffset - width
                : centre.X + HorizontalOffset;

            return
            [
                CandidateAt(insideX, centre.Y - height - VerticalOffset,
                    GatePinLabelPlacementKind.InsideAbove, 0),
                CandidateAt(insideX, centre.Y + VerticalOffset,
                    GatePinLabelPlacementKind.InsideBelow, 1),
                CandidateAt(outsideX, centre.Y - height - VerticalOffset,
                    GatePinLabelPlacementKind.OutsideAbove, 2),
                CandidateAt(outsideX, centre.Y + VerticalOffset,
                    GatePinLabelPlacementKind.OutsideBelow, 3),
            ];

            Candidate CandidateAt(
                double x,
                double y,
                GatePinLabelPlacementKind kind,
                int order)
            {
                Point origin = new(x, y);
                Rect worldBounds = new(
                    x - BackgroundPaddingX,
                    y - BackgroundPaddingY,
                    width + BackgroundPaddingX * 2,
                    height + BackgroundPaddingY * 2);
                Rect screenBounds = ToScreen(worldBounds, worldViewport, zoom)
                    .Inflate(ScreenGap);
                return new Candidate(origin, worldBounds, screenBounds, kind, order);
            }
        }
    }

    private static Rect ToScreen(Rect world, Rect viewport, double zoom) =>
        new(
            (world.X - viewport.X) * zoom,
            (world.Y - viewport.Y) * zoom,
            world.Width * zoom,
            world.Height * zoom);

    private sealed record Candidate(
        Point WorldOrigin,
        Rect WorldBounds,
        Rect ScreenBounds,
        GatePinLabelPlacementKind Kind,
        int Order);

    private sealed record OccupiedRect(
        Rect Bounds,
        string? AllowedOwnerNodeId,
        string? AllowedRequestId);

    private sealed class ScreenOccupancyIndex
    {
        private readonly double _cellSize;
        private readonly List<OccupiedRect> _entries = [];
        private readonly Dictionary<(int X, int Y), List<int>> _cells = [];
        private readonly HashSet<int> _queryBuffer = [];

        public ScreenOccupancyIndex(double cellSize)
        {
            _cellSize = cellSize;
        }

        public void Add(OccupiedRect entry)
        {
            int index = _entries.Count;
            _entries.Add(entry);
            foreach ((int x, int y) in CellsFor(entry.Bounds))
            {
                if (!_cells.TryGetValue((x, y), out List<int>? entries))
                {
                    entries = [];
                    _cells[(x, y)] = entries;
                }
                entries.Add(index);
            }
        }

        public int CountIntersections(
            Rect bounds,
            GatePinLabelPlacementRequest request)
        {
            _queryBuffer.Clear();
            int count = 0;
            foreach ((int x, int y) in CellsFor(bounds))
            {
                if (!_cells.TryGetValue((x, y), out List<int>? entries))
                {
                    continue;
                }
                foreach (int index in entries)
                {
                    if (!_queryBuffer.Add(index))
                    {
                        continue;
                    }
                    OccupiedRect entry = _entries[index];
                    if (entry.AllowedOwnerNodeId is not null
                        && string.Equals(
                            entry.AllowedOwnerNodeId,
                            request.NodeId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (entry.AllowedRequestId is not null
                        && string.Equals(
                            entry.AllowedRequestId,
                            request.Id,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (entry.Bounds.Intersects(bounds))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private IEnumerable<(int X, int Y)> CellsFor(Rect bounds)
        {
            int minX = (int)Math.Floor(bounds.Left / _cellSize);
            int maxX = (int)Math.Floor(bounds.Right / _cellSize);
            int minY = (int)Math.Floor(bounds.Top / _cellSize);
            int maxY = (int)Math.Floor(bounds.Bottom / _cellSize);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    yield return (x, y);
                }
            }
        }
    }
}

internal readonly record struct GateVisibleNode(
    ElkNode Node,
    double AbsoluteX,
    double AbsoluteY,
    Rect Bounds,
    int Order);

internal sealed class GateNodeSpatialIndex
{
    private const double CellSize = 256;
    private const int MaxCellsPerNode = 256;

    private readonly List<GateVisibleNode> _nodes = [];
    private readonly Dictionary<(int X, int Y), List<int>> _cells = [];
    private readonly List<int> _largeNodes = [];
    private readonly HashSet<int> _queryBuffer = [];

    private GateNodeSpatialIndex()
    {
    }

    public static GateNodeSpatialIndex Build(IReadOnlyList<ElkNode>? roots)
    {
        GateNodeSpatialIndex index = new();
        if (roots is null)
        {
            return index;
        }

        foreach (ElkNode root in roots)
        {
            index.AddRecursive(root, baseX: 0, baseY: 0);
        }
        return index;
    }

    public IReadOnlyList<GateVisibleNode> Query(Rect viewport)
    {
        _queryBuffer.Clear();
        foreach ((int x, int y) in CellsFor(viewport))
        {
            if (!_cells.TryGetValue((x, y), out List<int>? indices))
            {
                continue;
            }
            foreach (int index in indices)
            {
                _queryBuffer.Add(index);
            }
        }
        foreach (int index in _largeNodes)
        {
            _queryBuffer.Add(index);
        }

        return _queryBuffer
            .Select(index => _nodes[index])
            .Where(node => node.Bounds.Intersects(viewport))
            .OrderBy(static node => node.Order)
            .ToArray();
    }

    private void AddRecursive(ElkNode node, double baseX, double baseY)
    {
        double absoluteX = baseX + node.X;
        double absoluteY = baseY + node.Y;
        Rect bounds = new(absoluteX, absoluteY, node.Width, node.Height);
        int index = _nodes.Count;
        _nodes.Add(new GateVisibleNode(node, absoluteX, absoluteY, bounds, index));

        (int minX, int maxX, int minY, int maxY) = CellRange(bounds);
        long cellCount = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (cellCount > MaxCellsPerNode)
        {
            _largeNodes.Add(index);
        }
        else
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!_cells.TryGetValue((x, y), out List<int>? entries))
                    {
                        entries = [];
                        _cells[(x, y)] = entries;
                    }
                    entries.Add(index);
                }
            }
        }

        if (node.Children is not { Count: > 0 })
        {
            return;
        }
        foreach (ElkNode child in node.Children)
        {
            AddRecursive(child, absoluteX, absoluteY);
        }
    }

    private static IEnumerable<(int X, int Y)> CellsFor(Rect bounds)
    {
        (int minX, int maxX, int minY, int maxY) = CellRange(bounds);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                yield return (x, y);
            }
        }
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) CellRange(Rect bounds) =>
        (
            (int)Math.Floor(bounds.Left / CellSize),
            (int)Math.Floor(bounds.Right / CellSize),
            (int)Math.Floor(bounds.Top / CellSize),
            (int)Math.Floor(bounds.Bottom / CellSize)
        );
}
