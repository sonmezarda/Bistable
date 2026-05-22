using Avalonia;

namespace Bistable.App.Services.Routing;

internal sealed class HananGrid
{
    private const double MinSpacing = 1.5;

    public HananGrid(Rect bounds, IEnumerable<double> xCoords, IEnumerable<double> yCoords)
    {
        Bounds = bounds;
        XLines = Densify(bounds.X, bounds.Right, xCoords);
        YLines = Densify(bounds.Y, bounds.Bottom, yCoords);
    }

    public Rect Bounds { get; }

    public IReadOnlyList<double> XLines { get; }

    public IReadOnlyList<double> YLines { get; }

    public int ColumnCount => XLines.Count;

    public int RowCount => YLines.Count;

    public Point ToWorld(int column, int row) => new(XLines[column], YLines[row]);

    public (int Column, int Row) NearestCell(Point point)
    {
        return (ClosestIndex(XLines, point.X), ClosestIndex(YLines, point.Y));
    }

    private static IReadOnlyList<double> Densify(double min, double max, IEnumerable<double> coords)
    {
        List<double> values = new() { min, max };
        foreach (double value in coords)
        {
            if (value >= min - 0.5 && value <= max + 0.5)
            {
                values.Add(Math.Clamp(value, min, max));
            }
        }

        values.Sort();
        List<double> result = [values[0]];
        for (int index = 1; index < values.Count; index++)
        {
            if (values[index] - result[^1] >= MinSpacing)
            {
                result.Add(values[index]);
            }
        }

        if (result.Count == 1 || result[^1] < max - MinSpacing / 2)
        {
            result.Add(max);
        }

        return result;
    }

    private static int ClosestIndex(IReadOnlyList<double> sorted, double value)
    {
        int best = 0;
        double bestDistance = Math.Abs(sorted[0] - value);
        for (int index = 1; index < sorted.Count; index++)
        {
            double distance = Math.Abs(sorted[index] - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }
}
