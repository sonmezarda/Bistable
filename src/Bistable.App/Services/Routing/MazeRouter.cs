using Avalonia;

namespace Bistable.App.Services.Routing;

internal sealed class MazeRouter
{
    private const double BendCost = 8;
    private const double CongestionWeight = 80;
    private const double InteriorOverlapThreshold = 0.5;
    private const double ObstacleProximityMargin = 10;

    public IReadOnlyList<Point>? FindPath(
        HananGrid grid,
        Point sourceWorld,
        Point targetWorld,
        IReadOnlyList<Rect> obstacles,
        CongestionMap congestion)
    {
        (int sourceCol, int sourceRow) = grid.NearestCell(sourceWorld);
        (int targetCol, int targetRow) = grid.NearestCell(targetWorld);
        if (sourceCol == targetCol && sourceRow == targetRow)
        {
            return [grid.ToWorld(sourceCol, sourceRow)];
        }

        Rect? sourceObstacle = FindOwningObstacle(sourceWorld, obstacles);
        Rect? targetObstacle = FindOwningObstacle(targetWorld, obstacles);

        Direction[] directions = [Direction.East, Direction.West, Direction.North, Direction.South];

        PriorityQueue<long, double> open = new();
        Dictionary<long, double> bestG = [];
        Dictionary<long, long> cameFrom = [];
        Dictionary<long, Direction> incomingDir = [];

        long startKey = Encode(sourceCol, sourceRow);
        long targetKey = Encode(targetCol, targetRow);
        Point targetPoint = grid.ToWorld(targetCol, targetRow);

        bestG[startKey] = 0;
        incomingDir[startKey] = Direction.None;
        open.Enqueue(startKey, Manhattan(grid.ToWorld(sourceCol, sourceRow), targetPoint));

        while (open.TryDequeue(out long currentKey, out _))
        {
            if (currentKey == targetKey)
            {
                return Reconstruct(grid, cameFrom, currentKey);
            }

            double currentG = bestG[currentKey];
            (int currentCol, int currentRow) = Decode(currentKey);
            Direction parentDir = incomingDir.GetValueOrDefault(currentKey, Direction.None);
            Point currentPoint = grid.ToWorld(currentCol, currentRow);
            bool currentIsSource = currentCol == sourceCol && currentRow == sourceRow;
            bool currentIsTarget = currentCol == targetCol && currentRow == targetRow;

            foreach (Direction direction in directions)
            {
                (int dCol, int dRow) = Offset(direction);
                int nCol = currentCol + dCol;
                int nRow = currentRow + dRow;
                if (nCol < 0 || nCol >= grid.ColumnCount || nRow < 0 || nRow >= grid.RowCount)
                {
                    continue;
                }

                Point neighborPoint = grid.ToWorld(nCol, nRow);
                bool neighborIsSource = nCol == sourceCol && nRow == sourceRow;
                bool neighborIsTarget = nCol == targetCol && nRow == targetRow;
                bool touchesSource = currentIsSource || neighborIsSource;
                bool touchesTarget = currentIsTarget || neighborIsTarget;
                if (IsEdgeBlocked(currentPoint, neighborPoint, obstacles, touchesSource ? sourceObstacle : null, touchesTarget ? targetObstacle : null))
                {
                    continue;
                }

                long neighborKey = Encode(nCol, nRow);
                double edgeCost = Manhattan(currentPoint, neighborPoint);
                if (parentDir != Direction.None && parentDir != direction)
                {
                    edgeCost += BendCost;
                }

                edgeCost += congestion.Get(nCol, nRow) * CongestionWeight;
                double tentativeG = currentG + edgeCost;
                if (bestG.TryGetValue(neighborKey, out double existingG) && tentativeG >= existingG)
                {
                    continue;
                }

                bestG[neighborKey] = tentativeG;
                cameFrom[neighborKey] = currentKey;
                incomingDir[neighborKey] = direction;
                double heuristic = Manhattan(neighborPoint, targetPoint);
                open.Enqueue(neighborKey, tentativeG + heuristic);
            }
        }

        return null;
    }

    private static IReadOnlyList<Point> Reconstruct(HananGrid grid, IReadOnlyDictionary<long, long> cameFrom, long endKey)
    {
        List<Point> reverse = [];
        long current = endKey;
        while (true)
        {
            (int col, int row) = Decode(current);
            reverse.Add(grid.ToWorld(col, row));
            if (!cameFrom.TryGetValue(current, out long previous))
            {
                break;
            }

            current = previous;
        }

        reverse.Reverse();
        return CompactCollinear(reverse);
    }

    private static IReadOnlyList<Point> CompactCollinear(IReadOnlyList<Point> points)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        List<Point> result = [points[0]];
        for (int index = 1; index < points.Count - 1; index++)
        {
            Point previous = result[^1];
            Point current = points[index];
            Point next = points[index + 1];
            bool collinearHorizontal = Math.Abs(previous.Y - current.Y) < 0.01 && Math.Abs(current.Y - next.Y) < 0.01;
            bool collinearVertical = Math.Abs(previous.X - current.X) < 0.01 && Math.Abs(current.X - next.X) < 0.01;
            if (!collinearHorizontal && !collinearVertical)
            {
                result.Add(current);
            }
        }

        result.Add(points[^1]);
        return result;
    }

    private static bool IsEdgeBlocked(
        Point start,
        Point end,
        IReadOnlyList<Rect> obstacles,
        Rect? exemptSource,
        Rect? exemptTarget)
    {
        for (int index = 0; index < obstacles.Count; index++)
        {
            Rect obstacle = obstacles[index];
            if (exemptSource is Rect source && RectsEquivalent(obstacle, source))
            {
                continue;
            }

            if (exemptTarget is Rect target && RectsEquivalent(obstacle, target))
            {
                continue;
            }

            if (EdgeCrossesInterior(start, end, obstacle))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EdgeCrossesInterior(Point a, Point b, Rect obstacle)
    {
        Rect inflated = obstacle.Inflate(ObstacleProximityMargin);
        if (Math.Abs(a.Y - b.Y) < 0.01)
        {
            double y = a.Y;
            if (y <= inflated.Y + 0.01 || y >= inflated.Bottom - 0.01)
            {
                return false;
            }

            double minX = Math.Min(a.X, b.X);
            double maxX = Math.Max(a.X, b.X);
            double overlap = Math.Min(maxX, inflated.Right) - Math.Max(minX, inflated.X);
            return overlap > InteriorOverlapThreshold;
        }

        if (Math.Abs(a.X - b.X) < 0.01)
        {
            double x = a.X;
            if (x <= inflated.X + 0.01 || x >= inflated.Right - 0.01)
            {
                return false;
            }

            double minY = Math.Min(a.Y, b.Y);
            double maxY = Math.Max(a.Y, b.Y);
            double overlap = Math.Min(maxY, inflated.Bottom) - Math.Max(minY, inflated.Y);
            return overlap > InteriorOverlapThreshold;
        }

        return false;
    }

    private static Rect? FindOwningObstacle(Point pin, IReadOnlyList<Rect> obstacles)
    {
        foreach (Rect obstacle in obstacles)
        {
            bool onVerticalEdge = (Math.Abs(pin.X - obstacle.X) < 1 || Math.Abs(pin.X - obstacle.Right) < 1)
                && pin.Y >= obstacle.Y - 1 && pin.Y <= obstacle.Bottom + 1;
            bool onHorizontalEdge = (Math.Abs(pin.Y - obstacle.Y) < 1 || Math.Abs(pin.Y - obstacle.Bottom) < 1)
                && pin.X >= obstacle.X - 1 && pin.X <= obstacle.Right + 1;
            if (onVerticalEdge || onHorizontalEdge)
            {
                return obstacle;
            }

            if (obstacle.Contains(pin))
            {
                return obstacle;
            }
        }

        return null;
    }

    private static bool RectsEquivalent(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 0.5
        && Math.Abs(first.Y - second.Y) < 0.5
        && Math.Abs(first.Width - second.Width) < 0.5
        && Math.Abs(first.Height - second.Height) < 0.5;

    private static double Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static (int Col, int Row) Offset(Direction direction) => direction switch
    {
        Direction.East => (1, 0),
        Direction.West => (-1, 0),
        Direction.North => (0, -1),
        Direction.South => (0, 1),
        _ => (0, 0)
    };

    private static long Encode(int col, int row) => ((long)col << 32) | (uint)row;

    private static (int Col, int Row) Decode(long key) => ((int)(key >> 32), (int)(uint)key);

    private enum Direction
    {
        None,
        East,
        West,
        North,
        South
    }
}
