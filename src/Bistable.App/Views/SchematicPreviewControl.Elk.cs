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
        IReadOnlyList<DesignContAssign> contAssigns,
        IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>? scopePrimitives = null,
        IReadOnlyDictionary<string, IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>>? scopePrimitivesByModule = null)
    {
        Rect panel = ComputeElkPanelRect(bounds, moduleRect);
        _lastFocusedScopePanelRect = panel;

        DrawElkPanelChrome(context, panel);

        HashSet<string> expandedPaths = ExpandedScopePaths is null
            ? []
            : new HashSet<string>(ExpandedScopePaths, StringComparer.OrdinalIgnoreCase);
        ElkScopeData scope = new(scopePorts, childScopes, localSignals, contAssigns, expandedPaths, scopePrimitives, scopePrimitivesByModule);
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
        IReadOnlyDictionary<string, string> signalValues = BuildSignalValueLookup(contAssigns);
        ElkEdgeCoordinateContext coordinateContext = BuildEdgeCoordinateContext(layoutResult.Graph);
        SchematicLabelPlacementContext labelPlacement = new();

        DrawElkNodeBackgrounds(context, layoutResult.Graph, transform);
        DrawElkEdges(context, layoutResult.Graph, transform, signalValues, coordinateContext);
        DrawElkNodeForegrounds(context, layoutResult.Graph, transform, labelPlacement);
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

    private void DrawElkNodeBackgrounds(DrawingContext context, ElkGraph graph, ElkTransform transform)
    {
        DrawElkNodeBackgroundsRecursive(context, graph.Children, transform, baseX: 0, baseY: 0);
    }

    private void DrawElkNodeForegrounds(
        DrawingContext context,
        ElkGraph graph,
        ElkTransform transform,
        SchematicLabelPlacementContext labelPlacement)
    {
        DrawElkNodeForegroundsRecursive(context, graph.Children, transform, labelPlacement, baseX: 0, baseY: 0);
    }

    // ELK's compound layout positions sub-children relative to their parent node, so the
    // recursion accumulates absolute coordinates (baseX/baseY) before applying the global
    // viewport transform. Without this, nested children would render at the same root-origin
    // as their parent's coordinate.
    private void DrawElkNodeBackgroundsRecursive(
        DrawingContext context,
        IList<ElkNode> nodes,
        ElkTransform transform,
        double baseX,
        double baseY)
    {
        foreach (ElkNode node in nodes)
        {
            double absX = baseX + node.X;
            double absY = baseY + node.Y;
            if (IsElkCardNode(node))
            {
                Rect rect = transform.Apply(absX, absY, node.Width, node.Height);
                DrawElkNodeCardBackground(context, rect);
            }

            if (node.Children is { Count: > 0 } childNodes)
            {
                DrawElkNodeBackgroundsRecursive(context, childNodes, transform, absX, absY);
            }
        }
    }

    private void DrawElkNodeForegroundsRecursive(
        DrawingContext context,
        IList<ElkNode> nodes,
        ElkTransform transform,
        SchematicLabelPlacementContext labelPlacement,
        double baseX,
        double baseY)
    {
        foreach (ElkNode node in nodes)
        {
            double absX = baseX + node.X;
            double absY = baseY + node.Y;
            Rect rect = transform.Apply(absX, absY, node.Width, node.Height);

            if (node.Id is ElkNodeIds.BoundaryIn)
            {
                DrawElkBoundaryPins(context, node, rect, transform.Scale, isInput: true, labelPlacement);
            }
            else if (node.Id is ElkNodeIds.BoundaryOut)
            {
                DrawElkBoundaryPins(context, node, rect, transform.Scale, isInput: false, labelPlacement);
            }
            else if (ElkNodeIds.IsOperator(node.Id))
            {
                DrawElkOperatorNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsSplitter(node.Id))
            {
                DrawElkSplitterNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsJoiner(node.Id))
            {
                DrawElkJoinerNode(context, node, rect, transform.Scale);
            }
            else if (ElkNodeIds.IsFlipFlop(node.Id))
            {
                DrawElkFlipFlopNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsMux(node.Id))
            {
                DrawElkMuxNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsLatch(node.Id))
            {
                DrawElkLatchNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsMemory(node.Id))
            {
                DrawElkMemoryNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsBuffer(node.Id))
            {
                DrawElkBufferNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsConstantTie(node.Id))
            {
                DrawElkConstantTieNode(context, node, rect);
            }
            else if (ElkNodeIds.IsTriState(node.Id))
            {
                DrawElkTriStateNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsInverter(node.Id))
            {
                DrawElkInverterNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsGate(node.Id))
            {
                DrawElkGateNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsArith(node.Id))
            {
                DrawElkArithNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else if (ElkNodeIds.IsStructFanOut(node.Id))
            {
                DrawElkStructFanOutNode(context, node, rect, transform.Scale, labelPlacement);
            }
            else
            {
                DrawElkNodeCardForeground(context, node, rect, transform.Scale, labelPlacement);
                DrawElkChildExpansionButton(context, node, rect);
            }

            if (node.Children is { Count: > 0 } childNodes)
            {
                DrawElkNodeForegroundsRecursive(context, childNodes, transform, labelPlacement, absX, absY);
            }
        }
    }

    private static bool IsElkCardNode(ElkNode node) =>
        node.Id is not ElkNodeIds.BoundaryIn
        && node.Id is not ElkNodeIds.BoundaryOut
        && !ElkNodeIds.IsOperator(node.Id)
        && !ElkNodeIds.IsSplitter(node.Id)
        && !ElkNodeIds.IsJoiner(node.Id)
        && !ElkNodeIds.IsFlipFlop(node.Id)
        && !ElkNodeIds.IsMux(node.Id)
        && !ElkNodeIds.IsLatch(node.Id)
        && !ElkNodeIds.IsMemory(node.Id)
        && !ElkNodeIds.IsBuffer(node.Id)
        && !ElkNodeIds.IsConstantTie(node.Id)
        && !ElkNodeIds.IsTriState(node.Id)
        && !ElkNodeIds.IsInverter(node.Id)
        && !ElkNodeIds.IsGate(node.Id)
        && !ElkNodeIds.IsArith(node.Id)
        && !ElkNodeIds.IsStructFanOut(node.Id);

    private void DrawElkChildExpansionButton(DrawingContext context, ElkNode node, Rect rect)
    {
        if (!ElkGraphBuilder.IsExpandableChild(node)) return;
        if (!ElkGraphBuilder.TryGetHierarchyPath(node, out string hierarchyPath)) return;

        bool isExpanded = node.Children is { Count: > 0 };
        // DrawScopeExpansionButton lives in Rendering.cs; it draws the +/- glyph and
        // registers an expansion hit-target keyed by the hierarchy path.
        DrawScopeExpansionButton(context, rect, hierarchyPath, expanded: isExpanded);
    }

    private void DrawElkOperatorNode(
        DrawingContext context,
        ElkNode node,
        Rect rect,
        double scale,
        SchematicLabelPlacementContext labelPlacement)
    {
        string? symbol = node.Labels is { Count: > 0 } ? node.Labels[0].Text : null;
        Pen gatePen = new(Palette.ModuleStroke, 1.5);

        switch (symbol)
        {
            case "&" or "&&": DrawAndGate(context, rect, gatePen); break;
            case "|" or "||": DrawOrGate(context, rect, gatePen, xor: false); break;
            case "^":         DrawOrGate(context, rect, gatePen, xor: true);  break;
            case "~" or "!":  DrawNotGate(context, rect, gatePen);            break;
            default:          DrawOperatorBox(context, rect, symbol, gatePen); break;
        }

        if (node.Ports is not null)
        {
            foreach (ElkPort port in node.Ports)
                DrawElkPort(context, rect, port, scale, node.Width, labelPlacement);
        }
    }

    // AND gate — flat left edge + D-shaped semicircle on the right.
    private void DrawAndGate(DrawingContext context, Rect r, Pen stroke)
    {
        const double k = 0.5523; // Bezier quarter-circle approximation constant
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        double cx = x + w * 0.5, cy = y + h * 0.5;
        double rx = w * 0.5,     ry = h * 0.5;

        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(x, y), isFilled: true);
            gc.LineTo(new Point(cx, y));
            gc.CubicBezierTo(new Point(cx + rx * k, y),    new Point(x + w, cy - ry * k), new Point(x + w, cy));
            gc.CubicBezierTo(new Point(x + w, cy + ry * k), new Point(cx + rx * k, y + h), new Point(cx, y + h));
            gc.LineTo(new Point(x, y + h));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, geo);
    }

    // OR gate — torpedo/shield shape. XOR adds a second concave arc at the input side.
    private void DrawOrGate(DrawingContext context, Rect r, Pen stroke, bool xor)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        double xorIn = xor ? w * 0.18 : 0; // OR body shifts right for XOR
        double bx = x + xorIn;
        double bw = w - xorIn;

        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(bx, y), isFilled: true);
            gc.CubicBezierTo(new Point(bx + bw * 0.55, y),      new Point(x + w, y + h * 0.3),   new Point(x + w, y + h * 0.5));
            gc.CubicBezierTo(new Point(x + w, y + h * 0.7),     new Point(bx + bw * 0.55, y + h), new Point(bx, y + h));
            gc.CubicBezierTo(new Point(bx + bw * 0.2, y + h * 0.75), new Point(bx + bw * 0.2, y + h * 0.25), new Point(bx, y));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, geo);

        if (xor)
        {
            // XOR indicator: extra concave open arc just left of the body
            StreamGeometry arc = new();
            using (StreamGeometryContext gc = arc.Open())
            {
                gc.BeginFigure(new Point(x, y), isFilled: false);
                gc.CubicBezierTo(
                    new Point(x + bw * 0.2, y + h * 0.25),
                    new Point(x + bw * 0.2, y + h * 0.75),
                    new Point(x, y + h));
                gc.EndFigure(isClosed: false);
            }
            context.DrawGeometry(null, stroke, arc);
        }
    }

    // NOT gate — right-pointing triangle with output bubble.
    private void DrawNotGate(DrawingContext context, Rect r, Pen stroke)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        double bubbleR = w * 0.1;
        double tipX    = r.Right - bubbleR * 2;
        double midY    = y + h * 0.5;

        StreamGeometry tri = new();
        using (StreamGeometryContext gc = tri.Open())
        {
            gc.BeginFigure(new Point(x, y), isFilled: true);
            gc.LineTo(new Point(tipX, midY));
            gc.LineTo(new Point(x, y + h));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, tri);
        context.DrawEllipse(Palette.NodeFill, stroke, new Point(tipX + bubbleR, midY), bubbleR, bubbleR);
    }

    // Arithmetic / comparison / unknown operators — plain box with centred symbol text.
    private void DrawOperatorBox(DrawingContext context, Rect r, string? symbol, Pen stroke)
    {
        context.DrawRectangle(Palette.NodeFill, stroke, r.Deflate(1));
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            double fontSize = Math.Clamp(r.Height * 0.38, 7, 13);
            double textW    = MeasureLabelWidth(symbol, fontSize);
            DrawText(context, symbol, r.X + (r.Width - textW) / 2, r.Y + r.Height * 0.5 - fontSize * 0.6, Palette.Text, fontSize);
        }
    }

    // Left-pointing wedge (mirror of splitter): flat left edge holds WEST inputs (the
    // concat operands, MSB-first by Verilog convention), right apex emits the joined bus
    // on the single EAST output.
    private void DrawElkJoinerNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        double midY = rect.Y + rect.Height / 2;
        double indentX = Math.Min(rect.Width * 0.32, 10 * scale);

        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), isFilled: true);
            gc.LineTo(new Point(rect.Right - indentX, rect.Y));
            gc.LineTo(new Point(rect.Right, midY));
            gc.LineTo(new Point(rect.Right - indentX, rect.Bottom));
            gc.LineTo(new Point(rect.X, rect.Bottom));
            gc.EndFigure(isClosed: true);
        }

        context.DrawGeometry(Palette.NodeFill, new Pen(Palette.ModuleStroke, 1.2), geo);

        if (node.Ports is null) return;

        foreach (ElkPort port in node.Ports)
        {
            double px = rect.X + port.X * scale;
            double py = rect.Y + port.Y * scale;
            context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 2.2, 2.2);
        }
    }

    // Right-pointing wedge: left apex at the single WEST input, flat right edge at EAST outputs.
    // Bit-range labels are drawn inside the wedge near each output port (right-aligned to port).
    private void DrawElkSplitterNode(
        DrawingContext context,
        ElkNode node,
        Rect rect,
        double scale,
        SchematicLabelPlacementContext labelPlacement)
    {
        _ = labelPlacement;
        double midY = rect.Y + rect.Height / 2;
        double indentX = Math.Min(rect.Width * 0.32, 10 * scale);

        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, midY), isFilled: true);
            gc.LineTo(new Point(rect.X + indentX, rect.Y));
            gc.LineTo(new Point(rect.Right, rect.Y));
            gc.LineTo(new Point(rect.Right, rect.Bottom));
            gc.LineTo(new Point(rect.X + indentX, rect.Bottom));
            gc.EndFigure(isClosed: true);
        }

        context.DrawGeometry(Palette.NodeFill, new Pen(Palette.ModuleStroke, 1.2), geo);

        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            double px = rect.X + port.X * scale;
            double py = rect.Y + port.Y * scale;
            bool onEast = port.X >= node.Width - 1;

            context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 2.2, 2.2);

            // Bit-range labels go inside the wedge body (right-aligned to each EAST port).
            if (onEast && port.Labels is { Count: > 0 })
            {
                string label = port.Labels[0].Text;
                double fontSize = Math.Clamp(8 * scale, 7, 10);
                double labelW = MeasureLabelWidth(label, fontSize);
                double labelX = Math.Max(rect.X + indentX + 4 * scale, px - 6 * scale - labelW);
                double labelY = py - fontSize - 2 * scale;
                DrawText(context, label, labelX, labelY, Palette.PinStroke, fontSize);
            }
        }
    }

    // Boundary nodes remain in the ELK graph so the layered algorithm has anchors to route to,
    // but visually they are NOT cards: each port is drawn as a classic schematic pentagon
    // (`[>` for input, `>]` for output) attached to the outer scope frame. The cable polyline
    // already ends exactly at port.X * scale + nodeRect.X, which is the tip of the pentagon.
    private void DrawElkBoundaryPins(
        DrawingContext context,
        ElkNode node,
        Rect nodeRect,
        double scale,
        bool isInput,
        SchematicLabelPlacementContext labelPlacement)
    {
        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            Point tip = new(nodeRect.X + port.X * scale, nodeRect.Y + port.Y * scale);
            string label = port.Labels is { Count: > 0 } ? port.Labels[0].Text : string.Empty;
            // P2.6-4: a second label "INOUT" tags bidirectional ports so we
            // can draw a hexagon (arrows on both sides) instead of a pentagon.
            bool isInOut = port.Labels is { Count: > 1 }
                && string.Equals(port.Labels[1].Text, "INOUT", StringComparison.Ordinal);

            // P4 follow-up: append the live value to the label so the expanded
            // view's boundary pins read like `clk [1b] = 1` — same affordance
            // as the collapsed top symbol's value chips.
            string? portName = ExtractPortNameFromId(port.Id);
            SignalViewModel? signal = portName is null ? null : FindSignalByName(portName, isInput);
            if (signal?.Value is { } v && v != "-")
            {
                label = $"{label} = {v}";
            }

            DrawBoundaryPinGlyph(context, tip, label, isInput, isInOut, labelPlacement);

            // Make expanded-view boundary pins interactive the same way
            // collapsed-view pins are. Find the matching SignalViewModel and
            // register a SignalHitTarget so clicking on the pentagon either
            // toggles a 1-bit input or opens the bus editor.
            if (signal is not null) RegisterBoundaryPinSignalHit(port, tip, isInput, signal);
        }
    }

    private void RegisterBoundaryPinSignalHit(ElkPort port, Point tip, bool isInput, SignalViewModel signal)
    {
        _ = port;
        double w = CompactLayout ? 22 : 26;
        double h = CompactLayout ? 14 : 16;
        // Hit rect spans both the pentagon body AND a small margin around the
        // tip — generous so cursors aren't required to land precisely on the glyph.
        Rect hit = isInput
            ? new Rect(tip.X - w - 4, tip.Y - h / 2 - 4, w + 8, h + 8)
            : new Rect(tip.X - 4, tip.Y - h / 2 - 4, w + 8, h + 8);
        _signalHitTargets.Add(new SignalHitTarget(signal, hit));
    }

    private static string? ExtractPortNameFromId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        int dot = id.LastIndexOf('.');
        return dot < 0 || dot == id.Length - 1 ? null : id[(dot + 1)..];
    }

    private SignalViewModel? FindSignalByName(string portName, bool isInput)
    {
        if (Signals is null) return null;
        foreach (SignalViewModel signal in Signals)
        {
            if (string.Equals(signal.Name, portName, StringComparison.OrdinalIgnoreCase)
                && signal.IsInput == isInput)
            {
                return signal;
            }
        }
        return null;
    }

    private void DrawBoundaryPinGlyph(
        DrawingContext context,
        Point tip,
        string label,
        bool isInput,
        bool isInOut = false,
        SchematicLabelPlacementContext? labelPlacement = null)
    {
        _ = labelPlacement;
        double glyphWidth = CompactLayout ? 22 : 26;
        double glyphHeight = CompactLayout ? 14 : 16;
        IBrush stroke;
        if (isInOut)
        {
            stroke = new SolidColorBrush(Color.FromRgb(180, 140, 220));   // distinctive violet for bidir
        }
        else
        {
            stroke = isInput ? Palette.PinStroke : Palette.OutputValue;
        }

        // Pentagon outline: rectangle body + triangular tip. The tip sits at `tip`; the body
        // extends outward away from the design (left for inputs, right for outputs).
        // InOut uses a hexagon with triangular tips on BOTH sides.
        Point[] points;
        if (isInOut)
        {
            points = BuildInOutHexagon(tip, glyphWidth, glyphHeight);
        }
        else
        {
            points = isInput
                ? BuildInputPentagon(tip, glyphWidth, glyphHeight)
                : BuildOutputPentagon(tip, glyphWidth, glyphHeight);
        }

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
        double labelY = tip.Y - 14;
        DrawText(context, label, labelX, labelY, stroke, 10);
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

    /// <summary>
    /// P2.6-4: a horizontally-stretched hexagon with triangular tips on both
    /// the design-facing side (left) and the boundary side (right). Visually
    /// communicates that the port flows in both directions.
    /// </summary>
    private static Point[] BuildInOutHexagon(Point tip, double w, double h)
    {
        // tip is the design-facing apex (left). Body sits to the left of tip,
        // ending in another apex on the outside.
        double leftApexX = tip.X;
        double rightApexX = tip.X - w;
        double bodyLeftEdgeX = tip.X - w * 0.68;
        double bodyRightEdgeX = tip.X - w * 0.32;
        double topY = tip.Y - h / 2;
        double bottomY = tip.Y + h / 2;
        return
        [
            new Point(bodyRightEdgeX, topY),
            new Point(leftApexX, tip.Y),       // design-side apex
            new Point(bodyRightEdgeX, bottomY),
            new Point(bodyLeftEdgeX, bottomY),
            new Point(rightApexX, tip.Y),      // outside-side apex
            new Point(bodyLeftEdgeX, topY)
        ];
    }

    private void DrawElkNodeCardBackground(DrawingContext context, Rect rect)
    {
        context.FillRectangle(Palette.NodeFill, rect, 6);
    }

    private void DrawElkNodeCardForeground(
        DrawingContext context,
        ElkNode node,
        Rect rect,
        double scale,
        SchematicLabelPlacementContext labelPlacement)
    {
        // If this child node corresponds to the currently-selected hierarchy
        // scope, draw it with the accent stroke + a thicker pen so the user
        // sees at a glance which block matches their hierarchy selection.
        bool isSelectedScope = ElkGraphBuilder.TryGetHierarchyPath(node, out string hierarchyPath)
            && !string.IsNullOrWhiteSpace(ActiveScopePath)
            && string.Equals(hierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush stroke;
        if (node.Id is ElkNodeIds.BoundaryIn or ElkNodeIds.BoundaryOut)
        {
            stroke = Palette.PinStroke;
        }
        else
        {
            stroke = isSelectedScope ? Palette.Selected : Palette.ModuleStroke;
        }
        double strokeWidth = isSelectedScope ? 2.4 : 1.2;

        context.DrawRectangle(new Pen(stroke, strokeWidth), rect, 6);

        // Register a scope hit target so OnPointerPressed → HandleScopeHit
        // can route clicks to "select scope in hierarchy" / "enter sub-sim".
        // Without this the click would fall through and the user sees nothing.
        if (!string.IsNullOrWhiteSpace(hierarchyPath)
            && node.Id is not ElkNodeIds.BoundaryIn and not ElkNodeIds.BoundaryOut)
        {
            _scopeHitTargets.Add(new ScopeHitTarget(hierarchyPath, rect, CanExpand: ElkGraphBuilder.IsExpandableChild(node)));
        }

        // P2.5-2: title now sits with 8px top padding (was 4px) so its baseline
        // clears the first port row even when the port label is tall. Combined
        // with the bump to ModuleHeaderHeight=48 in the builder, the title and
        // first port are guaranteed not to overlap.
        if (node.Labels is { Count: > 0 })
        {
            string rawTitle = node.Labels[0].Text;
            double titleMaxWidth = Math.Max(40, rect.Width - 16);
            string title = Ellipsize(rawTitle, 11, titleMaxWidth);
            DrawText(context, title, rect.X + 8, rect.Y + 8, Palette.Text, 11);
        }

        if (node.Ports is null)
        {
            return;
        }

        foreach (ElkPort port in node.Ports)
        {
            DrawElkPort(context, rect, port, scale, node.Width, labelPlacement);
        }
    }

    // Port positions returned by ELK are relative to the parent node and are NOT pre-scaled,
    // so they must be multiplied by the active transform scale to align with the (already
    // scaled) module rect. Edge polylines arrive in root coordinates and are scaled separately
    // by ElkTransform.Apply, so port and edge endpoints converge on the same screen pixel.
    private void DrawElkPort(
        DrawingContext context,
        Rect nodeRect,
        ElkPort port,
        double scale,
        double nodeWidthUnscaled,
        SchematicLabelPlacementContext labelPlacement)
    {
        double px = nodeRect.X + port.X * scale;
        double py = nodeRect.Y + port.Y * scale;
        bool onEast = port.X >= nodeWidthUnscaled - 1;

        context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 2.2, 2.2);

        // Register a signal-reference hit target around the pin even when no
        // wire is incident on it (unconnected internal port pins). The second
        // label carries the connected signal name as embedded by the builder.
        if (port.Labels is { Count: > 1 } labels && !string.IsNullOrWhiteSpace(labels[1].Text))
        {
            Rect pinHit = new(px - 6, py - 6, 12, 12);
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(labels[1].Text, pinHit, null));
        }

        if (port.Labels is { Count: > 0 })
        {
            DrawElkPortLabel(context, nodeRect, port, scale, nodeWidthUnscaled, labelPlacement);
        }
    }

    private void DrawElkPortLabel(
        DrawingContext context,
        Rect nodeRect,
        ElkPort port,
        double scale,
        double nodeWidthUnscaled,
        SchematicLabelPlacementContext labelPlacement)
    {
        _ = labelPlacement;
        if (port.Labels is not { Count: > 0 })
        {
            return;
        }

        double px = nodeRect.X + port.X * scale;
        double py = nodeRect.Y + port.Y * scale;
        bool onEast = port.X >= nodeWidthUnscaled - 1;

        // P2.5-2: ellipsize long port labels so they never overlap with the
        // node's title region or the opposite-side ports. Available width is
        // half the node minus a safety margin (~12px), capped to a sensible max.
        string rawLabel = port.Labels[0].Text;
        double labelGap = 9;
        double maxLabelPx = Math.Max(40, nodeRect.Width * 0.5 - 12);
        string label = Ellipsize(rawLabel, 9, maxLabelPx);
        double fontSize = 9;
        double labelWidth = MeasureLabelWidth(label, fontSize);
        double labelX = onEast ? px - labelGap - labelWidth : px + labelGap;
        DrawText(context, label, labelX, py - fontSize - 3, Palette.PinStroke, fontSize);
    }

    private void DrawElkEdges(
        DrawingContext context,
        ElkGraph graph,
        ElkTransform transform,
        IReadOnlyDictionary<string, string> signalValues,
        ElkEdgeCoordinateContext coordinateContext)
    {
        bool anyHovered = !string.IsNullOrEmpty(_hoveredSignalName);
        // P4-3: pre-build a lookup of "which mux-input port is active right now"
        // so the edge renderer can highlight the wire that's currently selected.
        IReadOnlySet<string> activeMuxInputs = BuildActiveMuxInputSet(graph);
        foreach (ElkEdge edge in graph.Edges)
        {
            RenderElkEdge(context, edge, transform, signalValues, anyHovered, activeMuxInputs, coordinateContext);
        }
    }

    /// <summary>
    /// P4-3: walk every mux node, read its selector value through the
    /// LiveProbeService cache, and return the set of input-port IDs that
    /// correspond to the currently-selected branch. The edge renderer then
    /// thickens any edge whose target is in this set.
    /// </summary>
    private IReadOnlySet<string> BuildActiveMuxInputSet(ElkGraph graph)
    {
        HashSet<string> active = new(StringComparer.Ordinal);
        if (LiveProbes is null) return active;

        foreach (ElkNode node in graph.Children ?? [])
        {
            CollectActiveMuxInputs(node, active);
        }
        return active;
    }

    private void CollectActiveMuxInputs(ElkNode node, HashSet<string> active)
    {
        if (ElkNodeIds.IsMux(node.Id) && node.Labels is { Count: > 2 })
        {
            string selectorSignal = node.Labels[2].Text;
            if (!string.IsNullOrEmpty(selectorSignal))
            {
                string selPath = string.IsNullOrWhiteSpace(ActiveScopePath)
                    ? selectorSignal
                    : ActiveScopePath + "." + selectorSignal;
                string? selValue = LiveProbes?.GetCached(selPath);
                if (!string.IsNullOrWhiteSpace(selValue))
                {
                    MatchActiveMuxInput(node, selValue!, active);
                }
            }
        }
        if (node.Children is { Count: > 0 })
        {
            foreach (ElkNode child in node.Children) CollectActiveMuxInputs(child, active);
        }
    }

    /// <summary>
    /// For each west-side input port of the mux (Id ends with <c>.in.{N}</c>),
    /// compare its branch label (port.Labels[0]) with the parsed selector value.
    /// The branch label format produced by <c>DecodeMux</c> is "0"/"1" for binary
    /// muxes and "{signal}"/"else" for chained ones — we only match the numeric
    /// cases for now (chained-mux highlighting needs more semantic plumbing).
    /// </summary>
    private static void MatchActiveMuxInput(ElkNode muxNode, string selValue, HashSet<string> active)
    {
        if (muxNode.Ports is null) return;
        if (!TryParseSelectorValue(selValue, out ulong selNumeric)) return;

        foreach (ElkPort port in muxNode.Ports)
        {
            if (!port.Id.Contains(".in.", StringComparison.Ordinal)) continue;
            if (port.Labels is not { Count: > 0 }) continue;
            string branchLabel = port.Labels[0].Text;
            if (ulong.TryParse(branchLabel, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out ulong branchValue)
                && branchValue == selNumeric)
            {
                active.Add(port.Id);
                return;
            }
        }
    }

    private static bool TryParseSelectorValue(string text, out ulong value)
    {
        value = 0;
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return ulong.TryParse(text, out value);
    }

    private void RenderElkEdge(
        DrawingContext context,
        ElkEdge edge,
        ElkTransform transform,
        IReadOnlyDictionary<string, string> signalValues,
        bool anyHovered,
        IReadOnlySet<string> activeMuxInputs,
        ElkEdgeCoordinateContext coordinateContext)
    {
        if (edge.Sections is null || edge.Sections.Count == 0)
        {
            return;
        }

        string? signalName = edge.Labels is { Count: > 0 } ? edge.Labels[0].Text : null;
        int bitWidth = ReadEdgeBitWidth(edge);
        // P4-3: edge ends at an active mux input → thicker, accented pen.
        bool isActiveMuxPath = edge.Targets.Any(t => activeMuxInputs.Contains(t));
        ElkEdgeStyle style = BuildElkEdgeStyle(signalName, bitWidth, signalValues, anyHovered, isActiveMuxPath);
        Point coordinateOffset = ResolveEdgeCoordinateOffset(edge, coordinateContext);
        IReadOnlyList<Point> polyline = BuildEdgePolyline(edge.Sections, transform, coordinateOffset);

        IDisposable? dimScope = style.ShouldDim ? context.PushOpacity(0.22) : null;
        try
        {
            DrawPolyline(context, polyline, style.Pen);
            DrawJunctions(context, edge.JunctionPoints, transform, coordinateOffset, style.Pen.Brush!);
        }
        finally
        {
            dimScope?.Dispose();
        }

        if (!string.IsNullOrWhiteSpace(signalName))
        {
            _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signalName!, null, polyline));
        }

        // P4-1: mid-edge live value label. Suppressed for 1-bit signals
        // (already encoded by edge colour) and when no value is cached.
        // Internal signals come from LiveProbeService; top-level/port signals
        // come from the existing signalValues lookup so we render both.
        if (!string.IsNullOrWhiteSpace(signalName) && bitWidth > 1)
        {
            string? liveValue = LookupLiveValue(signalName!, signalValues);
            if (!string.IsNullOrWhiteSpace(liveValue) && liveValue != "-")
            {
                DrawEdgeLiveValueLabel(context, polyline, liveValue!);
            }
        }
    }

    /// <summary>
    /// Resolve a wire's live value. Top-level ports and trace signals are
    /// already in <paramref name="signalValues"/>; internal probes come from
    /// <see cref="LiveProbeService"/>'s cache (populated by VM's
    /// post-Eval/Tick refresh).
    /// </summary>
    private string? LookupLiveValue(string signalName, IReadOnlyDictionary<string, string> signalValues)
    {
        if (signalValues.TryGetValue(signalName, out string? snapshotValue)
            && !string.IsNullOrWhiteSpace(snapshotValue) && snapshotValue != "-")
        {
            return snapshotValue;
        }
        return LiveProbes?.GetCached(signalName);
    }

    /// <summary>
    /// Draw the hex value as a small chip near the midpoint of the polyline.
    /// Uses a dark filled pill behind the text so it stays legible against
    /// any wire colour (including the orange forced state).
    /// </summary>
    private void DrawEdgeLiveValueLabel(DrawingContext context, IReadOnlyList<Point> polyline, string value)
    {
        if (polyline.Count < 2) return;
        Point mid = PolylineMidpoint(polyline);
        string text = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value : "0x" + value;
        double textWidth = MeasureLabelWidth(text, 10);
        Rect pill = new(mid.X - textWidth / 2 - 4, mid.Y - 8, textWidth + 8, 14);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(190, 16, 22, 32)), pill, 3);
        DrawText(context, text, pill.X + 4, pill.Y, Palette.Text, 10);
    }

    private static Point PolylineMidpoint(IReadOnlyList<Point> polyline)
    {
        // Walk segments until we cross half the total length — keeps the
        // label centered on the visual path rather than on a single segment.
        double total = 0;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            total += Distance(polyline[i], polyline[i + 1]);
        }
        if (total <= 0) return polyline[0];
        double half = total / 2;
        double walked = 0;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            double seg = Distance(polyline[i], polyline[i + 1]);
            if (walked + seg >= half)
            {
                double t = (half - walked) / seg;
                return new Point(
                    polyline[i].X + (polyline[i + 1].X - polyline[i].X) * t,
                    polyline[i].Y + (polyline[i + 1].Y - polyline[i].Y) * t);
            }
            walked += seg;
        }
        return polyline[^1];
    }

    private static double Distance(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private ElkEdgeStyle BuildElkEdgeStyle(
        string? signalName,
        int bitWidth,
        IReadOnlyDictionary<string, string> signalValues,
        bool anyHovered,
        bool isActiveMuxPath = false)
    {
        bool isSelected = !string.IsNullOrWhiteSpace(signalName)
            && string.Equals(SelectedSignalName, signalName, StringComparison.OrdinalIgnoreCase);
        bool isHoveredNet = anyHovered
            && string.Equals(signalName, _hoveredSignalName, StringComparison.OrdinalIgnoreCase);
        bool shouldDim = anyHovered && !isHoveredNet && !isSelected;
        // Phase 3 force visualisation: pinned signals override the normal value
        // colour with a high-saturation orange so the user can see at a glance
        // that this wire is not following simulation.
        bool isForced = !string.IsNullOrWhiteSpace(signalName) && IsSignalForced(signalName!);

        IBrush brush;
        if (isForced)
        {
            brush = new SolidColorBrush(Color.FromRgb(255, 140, 60));
        }
        else if (isSelected)
        {
            brush = Palette.Selected;
        }
        else if (isActiveMuxPath)
        {
            // P4-3: active mux input — paint with a bright cyan so the active
            // data flow stands out from the other input wires. Sits below the
            // selected/forced precedence so user interactions still dominate.
            brush = new SolidColorBrush(Color.FromRgb(140, 220, 255));
        }
        else
        {
            brush = ResolveLogisimBrush(signalName, bitWidth, signalValues);
        }
        double thickness = ResolveEdgeThickness(bitWidth > 1, isSelected || isForced || isActiveMuxPath);
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

    private static void DrawJunctions(
        DrawingContext context,
        IReadOnlyList<ElkPoint>? junctions,
        ElkTransform transform,
        Point coordinateOffset,
        IBrush brush)
    {
        if (junctions is null || junctions.Count == 0)
        {
            return;
        }

        foreach (ElkPoint jp in junctions)
        {
            Point center = transform.Apply(jp, coordinateOffset);
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

    private static ElkEdgeCoordinateContext BuildEdgeCoordinateContext(ElkGraph graph)
    {
        Dictionary<string, string[]> endpointPaths = new(StringComparer.Ordinal);
        Dictionary<string, Point> nodeOrigins = new(StringComparer.Ordinal);
        Visit(graph.Children, [], absoluteX: 0, absoluteY: 0);
        return new ElkEdgeCoordinateContext(endpointPaths, nodeOrigins);

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
                    }
                }

                if (node.Children is { Count: > 0 })
                {
                    Visit(node.Children, nodePath, nodeAbsoluteX, nodeAbsoluteY);
                }
            }
        }
    }

    private static Point ResolveEdgeCoordinateOffset(ElkEdge edge, ElkEdgeCoordinateContext context)
    {
        string[]? commonPath = null;
        foreach (string endpoint in edge.Sources.Concat(edge.Targets))
        {
            if (!context.EndpointPaths.TryGetValue(endpoint, out string[]? endpointPath))
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

        if (commonPath is null || commonPath.Length == 0)
        {
            return default;
        }

        return context.NodeOrigins.TryGetValue(PathKey(commonPath), out Point origin)
            ? origin
            : default;
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

    private static IReadOnlyList<Point> BuildEdgePolyline(
        IReadOnlyList<ElkEdgeSection> sections,
        ElkTransform transform,
        Point coordinateOffset)
    {
        List<Point> points = [];
        foreach (ElkEdgeSection section in sections)
        {
            Point start = transform.Apply(section.StartPoint, coordinateOffset);
            if (points.Count == 0 || !AreClose(points[^1], start))
            {
                points.Add(start);
            }

            if (section.BendPoints is { Count: > 0 })
            {
                foreach (ElkPoint bp in section.BendPoints)
                {
                    points.Add(transform.Apply(bp, coordinateOffset));
                }
            }

            points.Add(transform.Apply(section.EndPoint, coordinateOffset));
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

        public Point Apply(ElkPoint p, Point coordinateOffset) =>
            new(
                OriginX + (p.X + coordinateOffset.X - GraphX) * Scale,
                OriginY + (p.Y + coordinateOffset.Y - GraphY) * Scale);
    }

    private sealed record ElkEdgeCoordinateContext(
        IReadOnlyDictionary<string, string[]> EndpointPaths,
        IReadOnlyDictionary<string, Point> NodeOrigins);
}
