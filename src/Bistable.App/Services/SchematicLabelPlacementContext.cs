using Avalonia;

namespace Bistable.App.Services;

/// <summary>
/// Lightweight screen-space label placer for schematic rendering.
/// Labels try their preferred position first, then nearby alternatives; wire
/// segments and already-placed labels are treated as obstacles.
/// </summary>
internal sealed class SchematicLabelPlacementContext
{
    private readonly List<Rect> _wireObstacles = [];
    private readonly List<Rect> _solidObstacles = [];
    private readonly List<Rect> _labelObstacles = [];

    public void AddWirePolyline(IReadOnlyList<Point> polyline, double padding = 3)
    {
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Point a = polyline[i];
            Point b = polyline[i + 1];
            double left = Math.Min(a.X, b.X);
            double top = Math.Min(a.Y, b.Y);
            double width = Math.Max(1, Math.Abs(a.X - b.X));
            double height = Math.Max(1, Math.Abs(a.Y - b.Y));
            _wireObstacles.Add(new Rect(left, top, width, height).Inflate(padding));
        }
    }

    public void AddSolidObstacle(Rect rect, double padding = 2)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        _solidObstacles.Add(rect.Inflate(padding));
    }

    public Rect PlaceLabel(Size size, IReadOnlyList<Point> candidateOrigins)
    {
        if (candidateOrigins.Count == 0)
        {
            return default;
        }

        Rect best = RectFromOrigin(candidateOrigins[0], size);
        double bestScore = double.PositiveInfinity;

        for (int i = 0; i < candidateOrigins.Count; i++)
        {
            Rect candidate = RectFromOrigin(candidateOrigins[i], size);
            double score = Score(candidate) + i * 0.01;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
                if (score <= 0)
                {
                    break;
                }
            }
        }

        _labelObstacles.Add(best.Inflate(2));
        return best;
    }

    private double Score(Rect rect)
    {
        Rect padded = rect.Inflate(1);
        double score = 0;
        foreach (Rect obstacle in _wireObstacles)
        {
            score += IntersectionArea(padded, obstacle) * 8;
        }

        foreach (Rect obstacle in _solidObstacles)
        {
            score += IntersectionArea(padded, obstacle) * 5;
        }

        foreach (Rect obstacle in _labelObstacles)
        {
            score += IntersectionArea(padded, obstacle) * 12;
        }

        return score;
    }

    private static Rect RectFromOrigin(Point origin, Size size) =>
        new(origin.X, origin.Y, Math.Max(1, size.Width), Math.Max(1, size.Height));

    private static double IntersectionArea(Rect a, Rect b)
    {
        double left = Math.Max(a.Left, b.Left);
        double right = Math.Min(a.Right, b.Right);
        double top = Math.Max(a.Top, b.Top);
        double bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top) return 0;
        return (right - left) * (bottom - top);
    }
}
