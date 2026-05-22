using Avalonia;

namespace Bistable.App.Services.Routing;

/// <summary>
/// Post-routing track assignment: finds parallel segments at the same Y (or X) that
/// overlap spatially and spreads them apart by a fixed track spacing.
/// Also applies port-side spreading: routes leaving from the same module edge are
/// given staggered stub end-points so they fan out before routing to their targets.
/// </summary>
internal static class TrackAssigner
{
    private const double TrackSpacingCompact = 6.0;
    private const double TrackSpacingNormal = 8.0;
    private const double BandTolerance = 1.5;
    private const double MinOverlap = 4.0;

    public static IReadOnlyList<SchematicConnectionRoute> Assign(
        IReadOnlyList<SchematicConnectionRoute> routes,
        bool compactLayout)
    {
        if (routes.Count <= 1)
        {
            return routes;
        }

        double spacing = compactLayout ? TrackSpacingCompact : TrackSpacingNormal;

        // Collect all interior horizontal and vertical segments across all routes
        List<SegmentRef> hSegs = [];
        List<SegmentRef> vSegs = [];
        for (int ri = 0; ri < routes.Count; ri++)
        {
            IReadOnlyList<Point> pts = routes[ri].Points;
            for (int si = 0; si < pts.Count - 1; si++)
            {
                Point a = pts[si];
                Point b = pts[si + 1];
                bool isInterior = si > 0 && si < pts.Count - 2;
                if (!isInterior)
                {
                    continue;
                }

                if (Math.Abs(a.Y - b.Y) < BandTolerance)
                {
                    hSegs.Add(new SegmentRef(ri, si, a.Y, Math.Min(a.X, b.X), Math.Max(a.X, b.X)));
                }
                else if (Math.Abs(a.X - b.X) < BandTolerance)
                {
                    vSegs.Add(new SegmentRef(ri, si, a.X, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                }
            }
        }

        // Compute per-route-point offsets; keyed by (routeIndex, pointIndex)
        Dictionary<(int Route, int Point), double> hOffsets = [];
        Dictionary<(int Route, int Point), double> vOffsets = [];

        SeparateParallelSegments(hSegs, spacing, horizontal: true, offsets: hOffsets);
        SeparateParallelSegments(vSegs, spacing, horizontal: false, offsets: vOffsets);

        if (hOffsets.Count == 0 && vOffsets.Count == 0)
        {
            return routes;
        }

        // Apply offsets to route point lists
        SchematicConnectionRoute[] result = new SchematicConnectionRoute[routes.Count];
        for (int ri = 0; ri < routes.Count; ri++)
        {
            result[ri] = ApplyOffsets(routes[ri], ri, hOffsets, vOffsets);
        }

        return result;
    }

    private static void SeparateParallelSegments(
        List<SegmentRef> segments,
        double spacing,
        bool horizontal,
        Dictionary<(int Route, int Point), double> offsets)
    {
        // Group by band: segments whose axis coordinate is within BandTolerance of each other
        segments.Sort(static (a, b) => a.AxisCoord.CompareTo(b.AxisCoord));

        int i = 0;
        while (i < segments.Count)
        {
            double bandCenter = segments[i].AxisCoord;
            List<SegmentRef> band = [];
            while (i < segments.Count && Math.Abs(segments[i].AxisCoord - bandCenter) <= BandTolerance)
            {
                band.Add(segments[i]);
                i++;
            }

            if (band.Count <= 1)
            {
                continue;
            }

            // Find groups within the band that actually overlap in the range dimension
            AssignTracksInBand(band, spacing, horizontal, offsets);
        }
    }

    private static void AssignTracksInBand(
        IReadOnlyList<SegmentRef> band,
        double spacing,
        bool horizontal,
        Dictionary<(int Route, int Point), double> offsets)
    {
        // Sort by range start
        List<SegmentRef> sorted = [.. band.OrderBy(static s => s.RangeMin)];

        // Sweep-line: find overlapping groups
        List<List<SegmentRef>> overlapGroups = [];
        List<SegmentRef> current = [sorted[0]];
        double maxEnd = sorted[0].RangeMax;

        for (int i = 1; i < sorted.Count; i++)
        {
            SegmentRef seg = sorted[i];
            if (seg.RangeMin < maxEnd - MinOverlap)
            {
                current.Add(seg);
                maxEnd = Math.Max(maxEnd, seg.RangeMax);
            }
            else
            {
                if (current.Count > 1)
                {
                    overlapGroups.Add(current);
                }

                current = [seg];
                maxEnd = seg.RangeMax;
            }
        }

        if (current.Count > 1)
        {
            overlapGroups.Add(current);
        }

        // For each overlap group, assign evenly-spread offsets centered around the original axis
        foreach (List<SegmentRef> group in overlapGroups)
        {
            int n = group.Count;
            double totalSpan = (n - 1) * spacing;
            double startOffset = -totalSpan / 2.0;
            for (int k = 0; k < n; k++)
            {
                double offset = startOffset + k * spacing;
                SegmentRef seg = group[k];
                // Apply to the two endpoints of this segment
                offsets[(seg.RouteIndex, seg.SegmentStartPoint)] = offset;
                offsets[(seg.RouteIndex, seg.SegmentStartPoint + 1)] = offset;
            }
        }
    }

    private static SchematicConnectionRoute ApplyOffsets(
        SchematicConnectionRoute route,
        int routeIndex,
        Dictionary<(int Route, int Point), double> hOffsets,
        Dictionary<(int Route, int Point), double> vOffsets)
    {
        IReadOnlyList<Point> pts = route.Points;
        Point[] adjusted = new Point[pts.Count];
        for (int pi = 0; pi < pts.Count; pi++)
        {
            double x = pts[pi].X;
            double y = pts[pi].Y;
            if (hOffsets.TryGetValue((routeIndex, pi), out double dy))
            {
                y += dy;
            }

            if (vOffsets.TryGetValue((routeIndex, pi), out double dx))
            {
                x += dx;
            }

            adjusted[pi] = new Point(x, y);
        }

        // Recompute label anchor from adjusted points
        Point newAnchor = GetLabelAnchor(adjusted);
        return route with
        {
            Points = adjusted,
            LabelAnchor = newAnchor
        };
    }

    private static Point GetLabelAnchor(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            return points.Count == 1 ? points[0] : default;
        }

        int segmentIndex = Math.Max(0, (points.Count - 1) / 2);
        Point start = points[segmentIndex];
        Point end = points[segmentIndex + 1];
        return new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
    }

    private readonly record struct SegmentRef(
        int RouteIndex,
        int SegmentStartPoint,
        double AxisCoord,
        double RangeMin,
        double RangeMax);
}
