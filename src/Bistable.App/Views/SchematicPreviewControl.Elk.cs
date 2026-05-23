using Avalonia;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
    private static readonly ElkSchematicEngine ElkEngine = new();

    private void DrawElkScopePanel(
        DrawingContext context,
        Rect bounds,
        Rect moduleRect,
        IReadOnlyList<SignalViewModel> scopeSignals,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        IReadOnlyList<DesignContAssign> contAssigns)
    {
        Rect panel = ComputeElkPanelRect(bounds, moduleRect);
        _lastFocusedScopePanelRect = panel;

        DrawElkPanelChrome(context, panel);

        ElkScopeData scope = new(scopePorts, childScopes, localSignals, contAssigns);
        ElkLayoutResult layoutResult;
        try
        {
            layoutResult = ElkEngine.Compute(scope, CompactLayout);
        }
        catch (SchematicRoutingException ex)
        {
            DrawElkErrorBanner(context, panel, ex.Message);
            DrawScopeProbeSummary(context, panel, scopeSignals);
            return;
        }

        Rect canvas = panel.Deflate(new Thickness(20, 76, 20, 36));
        ElkTransform transform = ComputeFitTransform(layoutResult.Graph, canvas);
        IReadOnlyDictionary<string, string> signalValues = BuildSignalValueLookup();

        DrawElkEdges(context, layoutResult.Graph, transform, signalValues);
        DrawElkNodes(context, layoutResult.Graph, transform);
        DrawScopeProbeSummary(context, panel, scopeSignals);
    }

    private static Rect ComputeElkPanelRect(Rect bounds, Rect moduleRect)
    {
        double margin = 16;
        double width = Math.Max(800, bounds.Width - margin * 2);
        double height = Math.Max(480, bounds.Height - moduleRect.Bottom - margin * 4);
        double x = Math.Max(margin, bounds.X + (bounds.Width - width) / 2);
        double y = moduleRect.Bottom + 32;
        return new Rect(x, y, width, height);
    }

    private void DrawElkPanelChrome(DrawingContext context, Rect panel)
    {
        context.FillRectangle(Palette.FocusPanelFill, panel, 8);
        context.DrawRectangle(new Pen(Palette.ModuleStroke, 1.2), panel, 8);
        DrawScopeExpansionButton(context, panel, ActiveScopePath, expanded: true);
        DrawText(context, string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!, panel.X + 18, panel.Y + 12, Palette.Text, 13);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, panel.Width - 36), panel.X + 18, panel.Y + 34, Palette.PinStroke, 11);
        if (!string.IsNullOrWhiteSpace(ActiveScopePath))
        {
            DrawText(context, Ellipsize(ActiveScopePath!, 10, panel.Width - 36), panel.X + 18, panel.Y + 52, Palette.Muted, 10);
        }
    }

    private void DrawElkErrorBanner(DrawingContext context, Rect panel, string message)
    {
        DrawText(context, "ELK router error", panel.X + 18, panel.Y + 82, Palette.Selected, 12);
        DrawText(context, Ellipsize(message, 10, panel.Width - 36), panel.X + 18, panel.Y + 102, Palette.Text, 10);
    }

    private static ElkTransform ComputeFitTransform(ElkGraph graph, Rect canvas)
    {
        double graphWidth = Math.Max(1, graph.Width);
        double graphHeight = Math.Max(1, graph.Height);
        double scale = Math.Min(canvas.Width / graphWidth, canvas.Height / graphHeight);
        scale = Math.Clamp(scale, 0.18, 1.6);
        double renderedWidth = graphWidth * scale;
        double renderedHeight = graphHeight * scale;
        double originX = canvas.X + Math.Max(0, (canvas.Width - renderedWidth) / 2);
        double originY = canvas.Y + Math.Max(0, (canvas.Height - renderedHeight) / 2);
        return new ElkTransform(originX, originY, scale, graph.X, graph.Y);
    }

    private void DrawElkNodes(DrawingContext context, ElkGraph graph, ElkTransform transform)
    {
        foreach (ElkNode node in graph.Children)
        {
            Rect rect = transform.Apply(node.X, node.Y, node.Width, node.Height);
            if (node.Id is ElkNodeIds.BoundaryIn)
            {
                DrawElkBoundaryPins(context, node, rect, transform.Scale, isInput: true);
            }
            else if (node.Id is ElkNodeIds.BoundaryOut)
            {
                DrawElkBoundaryPins(context, node, rect, transform.Scale, isInput: false);
            }
            else if (ElkNodeIds.IsOperator(node.Id))
            {
                DrawElkOperatorNode(context, node, rect, transform.Scale);
            }
            else
            {
                DrawElkNodeCard(context, node, rect, transform.Scale);
            }
        }
    }

    private void DrawElkOperatorNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        double cx = rect.X + rect.Width / 2;
        double cy = rect.Y + rect.Height / 2;
        double radius = Math.Min(rect.Width, rect.Height) / 2 - 1 * scale;

        context.DrawEllipse(Palette.NodeFill, new Pen(Palette.ModuleStroke, 1.4), new Point(cx, cy), radius, radius);

        string? symbol = node.Labels is { Count: > 0 } ? node.Labels[0].Text : null;
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            double fontSize = Math.Clamp(radius * 0.7, 8, 16);
            double textW = MeasureLabelWidth(symbol!, fontSize);
            DrawText(context, symbol!, cx - textW / 2, cy - fontSize * 0.6, Palette.Text, fontSize);
        }

        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            DrawElkPort(context, rect, port, scale, node.Width);
        }
    }

    // Boundary nodes remain in the ELK graph so the layered algorithm has anchors to route to,
    // but visually they are NOT cards: each port is drawn as a classic schematic pentagon
    // (`[>` for input, `>]` for output) attached to the outer scope frame. The cable polyline
    // already ends exactly at port.X * scale + nodeRect.X, which is the tip of the pentagon.
    private void DrawElkBoundaryPins(DrawingContext context, ElkNode node, Rect nodeRect, double scale, bool isInput)
    {
        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            Point tip = new(nodeRect.X + port.X * scale, nodeRect.Y + port.Y * scale);
            string label = port.Labels is { Count: > 0 } ? port.Labels[0].Text : string.Empty;
            DrawBoundaryPinGlyph(context, tip, label, isInput);
        }
    }

    private void DrawBoundaryPinGlyph(DrawingContext context, Point tip, string label, bool isInput)
    {
        double glyphWidth = CompactLayout ? 22 : 26;
        double glyphHeight = CompactLayout ? 14 : 16;
        IBrush stroke = isInput ? Palette.PinStroke : Palette.OutputValue;

        // Pentagon outline: rectangle body + triangular tip. The tip sits at `tip`; the body
        // extends outward away from the design (left for inputs, right for outputs).
        Point[] points = isInput
            ? BuildInputPentagon(tip, glyphWidth, glyphHeight)
            : BuildOutputPentagon(tip, glyphWidth, glyphHeight);

        StreamGeometry geometry = new();
        using (StreamGeometryContext gc = geometry.Open())
        {
            gc.BeginFigure(points[0], isFilled: true);
            for (int i = 1; i < points.Length; i++)
            {
                gc.LineTo(points[i]);
            }

            gc.EndFigure(isClosed: true);
        }

        context.DrawGeometry(Palette.NodeFill, new Pen(stroke, 1.3), geometry);

        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        double labelGap = 8;
        double labelWidth = MeasureLabelWidth(label, 10);
        double labelX = isInput
            ? tip.X - glyphWidth - labelGap - labelWidth
            : tip.X + glyphWidth + labelGap;
        DrawText(context, label, labelX, tip.Y - 6, stroke, 10);
    }

    private static Point[] BuildInputPentagon(Point tip, double w, double h)
    {
        // Body to the left, triangular tip on the right at `tip`.
        double left = tip.X - w;
        double topY = tip.Y - h / 2;
        double bottomY = tip.Y + h / 2;
        double bodyRight = tip.X - w * 0.32;
        return
        [
            new Point(left, topY),
            new Point(bodyRight, topY),
            new Point(tip.X, tip.Y),
            new Point(bodyRight, bottomY),
            new Point(left, bottomY)
        ];
    }

    private static Point[] BuildOutputPentagon(Point tip, double w, double h)
    {
        // Triangular point on the right (outward), rectangular body to the right of `tip`.
        double right = tip.X + w;
        double topY = tip.Y - h / 2;
        double bottomY = tip.Y + h / 2;
        double bodyLeft = tip.X + w * 0.32;
        return
        [
            new Point(bodyLeft, topY),
            new Point(right, topY),
            new Point(right, bottomY),
            new Point(bodyLeft, bottomY),
            new Point(tip.X, tip.Y)
        ];
    }

    private void DrawElkNodeCard(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        IBrush fill = Palette.NodeFill;
        IBrush stroke = node.Id is ElkNodeIds.BoundaryIn or ElkNodeIds.BoundaryOut
            ? Palette.PinStroke
            : Palette.ModuleStroke;

        context.FillRectangle(fill, rect, 6);
        context.DrawRectangle(new Pen(stroke, 1.2), rect, 6);

        if (node.Labels is { Count: > 0 })
        {
            DrawText(context, node.Labels[0].Text, rect.X + 8, rect.Y + 4, Palette.Text, 11);
        }

        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            DrawElkPort(context, rect, port, scale, node.Width);
        }
    }

    // Port positions returned by ELK are relative to the parent node and are NOT pre-scaled,
    // so they must be multiplied by the active transform scale to align with the (already
    // scaled) module rect. Edge polylines arrive in root coordinates and are scaled separately
    // by ElkTransform.Apply, so port and edge endpoints converge on the same screen pixel.
    private void DrawElkPort(DrawingContext context, Rect nodeRect, ElkPort port, double scale, double nodeWidthUnscaled)
    {
        double px = nodeRect.X + port.X * scale;
        double py = nodeRect.Y + port.Y * scale;
        bool onEast = port.X >= nodeWidthUnscaled - 1;

        context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 2.2, 2.2);

        if (port.Labels is { Count: > 0 })
        {
            string label = port.Labels[0].Text;
            double labelGap = 9;
            double labelX = onEast ? px - labelGap - MeasureLabelWidth(label, 10) : px + labelGap;
            DrawText(context, label, labelX, py - 6, Palette.PinStroke, 9);
        }
    }

    private void DrawElkEdges(
        DrawingContext context,
        ElkGraph graph,
        ElkTransform transform,
        IReadOnlyDictionary<string, string> signalValues)
    {
        bool anyHovered = !string.IsNullOrEmpty(_hoveredSignalName);
        foreach (ElkEdge edge in graph.Edges)
        {
            RenderElkEdge(context, edge, transform, signalValues, anyHovered);
        }
    }

    private void RenderElkEdge(
        DrawingContext context,
        ElkEdge edge,
        ElkTransform transform,
        IReadOnlyDictionary<string, string> signalValues,
        bool anyHovered)
    {
        if (edge.Sections is null || edge.Sections.Count == 0)
        {
            return;
        }

        string? signalName = edge.Labels is { Count: > 0 } ? edge.Labels[0].Text : null;
        int bitWidth = ReadEdgeBitWidth(edge);
        ElkEdgeStyle style = BuildElkEdgeStyle(signalName, bitWidth, signalValues, anyHovered);
        IReadOnlyList<Point> polyline = BuildEdgePolyline(edge.Sections, transform);

        IDisposable? dimScope = style.ShouldDim ? context.PushOpacity(0.22) : null;
        try
        {
            DrawPolyline(context, polyline, style.Pen);
            DrawJunctions(context, edge.JunctionPoints, transform, style.Pen.Brush!);
        }
        finally
        {
            dimScope?.Dispose();
        }

        if (!string.IsNullOrWhiteSpace(signalName))
        {
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signalName!, null, polyline));
        }
    }

    private ElkEdgeStyle BuildElkEdgeStyle(
        string? signalName,
        int bitWidth,
        IReadOnlyDictionary<string, string> signalValues,
        bool anyHovered)
    {
        bool isSelected = !string.IsNullOrWhiteSpace(signalName)
            && string.Equals(SelectedSignalName, signalName, StringComparison.OrdinalIgnoreCase);
        bool isHoveredNet = anyHovered
            && string.Equals(signalName, _hoveredSignalName, StringComparison.OrdinalIgnoreCase);
        bool shouldDim = anyHovered && !isHoveredNet && !isSelected;

        IBrush brush = isSelected
            ? Palette.Selected
            : ResolveLogisimBrush(signalName, bitWidth, signalValues);
        double thickness = ResolveEdgeThickness(bitWidth > 1, isSelected);
        Pen pen = new(brush, thickness, lineCap: PenLineCap.Square);
        return new ElkEdgeStyle(pen, shouldDim);
    }

    private static int ReadEdgeBitWidth(ElkEdge edge)
    {
        if (edge.Labels is { Count: > 1 }
            && int.TryParse(edge.Labels[1].Text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int width))
        {
            return Math.Max(1, width);
        }

        return 1;
    }

    private static void DrawPolyline(DrawingContext context, IReadOnlyList<Point> polyline, Pen pen)
    {
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            context.DrawLine(pen, polyline[i], polyline[i + 1]);
        }
    }

    private static void DrawJunctions(DrawingContext context, IReadOnlyList<ElkPoint>? junctions, ElkTransform transform, IBrush brush)
    {
        if (junctions is null || junctions.Count == 0)
        {
            return;
        }

        foreach (ElkPoint jp in junctions)
        {
            Point center = transform.Apply(jp);
            context.DrawEllipse(brush, null, center, 2.8, 2.8);
        }
    }

    private readonly record struct ElkEdgeStyle(Pen Pen, bool ShouldDim);

    // Logisim-Evolution-style palette: colour reflects signal *state*, not direction.
    //   1-bit 0  → LogicLow (dim green)
    //   1-bit 1  → LogicHigh (vivid green)
    //   bus 0    → BusInactive (muted gray)
    //   bus !=0  → BusActive (off-white)
    //   x / undefined → Unknown (red)
    //   z (high-impedance)  → HighZ (cyan)
    private IBrush ResolveLogisimBrush(
        string? signalName,
        int bitWidth,
        IReadOnlyDictionary<string, string> signalValues)
    {
        bool isBus = bitWidth > 1;
        if (string.IsNullOrWhiteSpace(signalName))
        {
            return isBus ? Palette.BusInactive : Palette.LogicLow;
        }

        signalValues.TryGetValue(signalName!, out string? value);

        if (IsHighZ(value))
        {
            return Palette.HighZ;
        }

        if (TryResolveRouteActivity(value, out bool isActive))
        {
            if (isBus)
            {
                return isActive ? Palette.BusActive : Palette.BusInactive;
            }

            return isActive ? Palette.LogicHigh : Palette.LogicLow;
        }

        if (value is not null)
        {
            // Value reported but unparseable (typically 'x').
            return Palette.Unknown;
        }

        // No value reported yet — render as quiescent state.
        return isBus ? Palette.BusInactive : Palette.LogicLow;
    }

    private static bool IsHighZ(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return trimmed.Equals("z", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("0z", StringComparison.OrdinalIgnoreCase);
    }

    private double ResolveEdgeThickness(bool isBus, bool isSelected)
    {
        double baseThickness = ResolveBaseEdgeThickness(isBus);
        return isSelected ? baseThickness + 0.8 : baseThickness;
    }

    private double ResolveBaseEdgeThickness(bool isBus)
    {
        if (isBus)
        {
            return CompactLayout ? 2.4 : 2.8;
        }

        return CompactLayout ? 1.2 : 1.4;
    }

    private static IReadOnlyList<Point> BuildEdgePolyline(IReadOnlyList<ElkEdgeSection> sections, ElkTransform transform)
    {
        List<Point> points = [];
        foreach (ElkEdgeSection section in sections)
        {
            Point start = transform.Apply(section.StartPoint);
            if (points.Count == 0 || !AreClose(points[^1], start))
            {
                points.Add(start);
            }

            if (section.BendPoints is { Count: > 0 })
            {
                foreach (ElkPoint bp in section.BendPoints)
                {
                    points.Add(transform.Apply(bp));
                }
            }

            points.Add(transform.Apply(section.EndPoint));
        }

        return points;
    }

    private static bool AreClose(Point a, Point b) =>
        Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5;

    private static double MeasureLabelWidth(string label, double fontSize) =>
        label.Length * fontSize * 0.58;

    private readonly record struct ElkTransform(double OriginX, double OriginY, double Scale, double GraphX, double GraphY)
    {
        public Rect Apply(double x, double y, double w, double h) =>
            new(OriginX + (x - GraphX) * Scale, OriginY + (y - GraphY) * Scale, w * Scale, h * Scale);

        public Point Apply(ElkPoint p) =>
            new(OriginX + (p.X - GraphX) * Scale, OriginY + (p.Y - GraphY) * Scale);
    }
}
