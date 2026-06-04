using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Design.Schematic;
using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.App.Views;

// Phase 6.5 Wave 1: a custom Avalonia control that renders an `ElkGraph`
// directly to a `DrawingContext`. No Avalonia child controls per node — that
// was the killer in the first cut where each cell + each edge added an
// Avalonia element and Layout choked on 2k+ items.
//
// Responsibilities:
//   - Receive a laid-out ElkGraph + the source GateModule (for cell-type lookup).
//   - Paint boundary anchors, cell symbols (AND / OR / XOR / NOT / BUF / MUX /
//     FF / Latch / Unknown), and edges with orthogonal polylines.
//   - Handle pan + zoom (middle-mouse drag + Ctrl-wheel + 'F' fit + 'R' reset).
//
// Hit testing + hierarchy + selection live in later waves; this control is
// deliberately stateless beyond pan/zoom so it can be replaced in-place.
public sealed class GateSchematicCanvas : Control
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;
    private const double FitPadding = 24;
    private static readonly IBrush CanvasBackground = SolidColorBrush.Parse("#0e141c");

    private ElkGraph? _graph;
    private GateModule? _module;
    private double _zoom = 1.0;
    private Point _pan;
    private Point _lastPointerPos;
    private bool _isPanning;
    private bool _fitPending = true;

    /// <summary>
    /// Phase 6.5 Wave 2: raised when the user double-clicks (or single-clicks
    /// the "+" badge on) a sub-module instance. The event argument is the
    /// instance name; the window translates it into a breadcrumb push.
    /// </summary>
    public event EventHandler<string>? SubModuleActivated;

    public GateSchematicCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Replace the rendered graph and force a re-fit on next draw.</summary>
    public void SetGraph(ElkGraph graph, GateModule module)
    {
        _graph = graph;
        _module = module;
        _fitPending = true;
        InvalidateVisual();
    }

    public void FitToView()
    {
        _fitPending = true;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _zoom = 1.0;
        _pan = new Point(FitPadding, FitPadding);
        _fitPending = false;
        InvalidateVisual();
    }

    // ── Render ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(CanvasBackground, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (_graph is null || _module is null) return;

        if (_fitPending) ApplyFit();

        using (context.PushTransform(Matrix.CreateTranslation(_pan.X, _pan.Y)))
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom)))
        {
            DrawEdges(context, _graph);
            DrawNodes(context, _graph, _module);
        }

        // Overlay HUD with zoom percentage.
        FormattedText hud = new(
            $"Zoom {(_zoom * 100):0}% · pan/zoom: middle-drag · Ctrl+wheel · F=fit · R=reset",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("monospace"),
            10,
            new SolidColorBrush(Color.FromRgb(143, 154, 173)));
        context.DrawText(hud, new Point(8, Bounds.Height - 18));
    }

    private void ApplyFit()
    {
        if (_graph is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double gw = Math.Max(1, _graph.Width);
        double gh = Math.Max(1, _graph.Height);
        double availW = Math.Max(1, Bounds.Width - FitPadding * 2);
        double availH = Math.Max(1, Bounds.Height - FitPadding * 2);
        _zoom = Math.Clamp(Math.Min(availW / gw, availH / gh), MinZoom, MaxZoom);
        double renderedW = gw * _zoom;
        double renderedH = gh * _zoom;
        _pan = new Point(
            Math.Max(FitPadding, (Bounds.Width - renderedW) / 2),
            Math.Max(FitPadding, (Bounds.Height - renderedH) / 2));
        _fitPending = false;
    }

    // ── Edges ─────────────────────────────────────────────────────────────

    private static readonly IBrush WireBrush = SolidColorBrush.Parse("#65d889");
    private static readonly Pen WirePen = new(WireBrush, 1.0);

    private static void DrawEdges(DrawingContext context, ElkGraph graph)
    {
        if (graph.Edges is null) return;
        foreach (ElkEdge edge in graph.Edges)
        {
            if (edge.Sections is null) continue;
            foreach (ElkEdgeSection section in edge.Sections)
            {
                Point prev = new(section.StartPoint.X, section.StartPoint.Y);
                if (section.BendPoints is { } bends)
                {
                    foreach (ElkPoint bp in bends)
                    {
                        Point cur = new(bp.X, bp.Y);
                        context.DrawLine(WirePen, prev, cur);
                        prev = cur;
                    }
                }
                context.DrawLine(WirePen, prev, new Point(section.EndPoint.X, section.EndPoint.Y));
            }
        }
    }

    // ── Nodes ─────────────────────────────────────────────────────────────

    private static readonly IBrush NodeStroke = SolidColorBrush.Parse("#5dbcff");
    private static readonly IBrush NodeFill   = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly Pen NodePen = new(NodeStroke, 1.2);

    private static void DrawNodes(DrawingContext context, ElkGraph graph, GateModule module)
    {
        if (graph.Children is null) return;

        // Build a quick lookup from node-id → original GateCell so we can use
        // GateCellLibrary to pick the right symbol painter.
        Dictionary<string, GateCell> cellByPrefixedName = new(StringComparer.Ordinal);
        foreach (GateCell cell in module.Cells)
        {
            cellByPrefixedName[cell.Name] = cell;
        }

        foreach (ElkNode node in graph.Children)
        {
            Rect rect = new(node.X, node.Y, node.Width, node.Height);
            DrawNodeBackground(context, node, rect);
            DrawNodeForeground(context, node, rect, cellByPrefixedName);
            DrawPorts(context, node);
        }
    }

    private static void DrawNodeBackground(DrawingContext ctx, ElkNode node, Rect rect)
    {
        if (node.Id is "boundary_in" or "boundary_out")
        {
            // Boundary anchors get a faint surround so the user can see them.
            ctx.DrawRectangle(NodeFill, new Pen(MutedBrush, 0.6) { DashStyle = DashStyle.Dash }, rect, 2, 2);
            return;
        }

        // For cell nodes we paint the body in DrawNodeForeground (symbol-aware).
        // Pre-fill with the node background so the painter doesn't have to
        // erase ports that overlap the body.
        ctx.FillRectangle(NodeFill, rect);
    }

    private static void DrawNodeForeground(
        DrawingContext ctx,
        ElkNode node,
        Rect rect,
        IReadOnlyDictionary<string, GateCell> cellByName)
    {
        if (node.Id is "boundary_in" or "boundary_out")
        {
            string label = node.Id == "boundary_in" ? "IN" : "OUT";
            DrawLabel(ctx, rect.X + 6, rect.Y + 4, label, MutedBrush, 9);
            return;
        }

        // Phase 6.5 Wave 2: sub-module instance — draw as Vivado-style
        // expandable block with instance name + module-type chip + "+" hint.
        if (node.Id.StartsWith("inst_", System.StringComparison.Ordinal))
        {
            DrawSubModuleInstance(ctx, node, rect);
            return;
        }

        // Find the original cell for this node — the builder's prefixes are
        // gate_/ff_/mux_/inv_/buf_/latch_/node_ followed by sanitized cell
        // name + cell index. We strip the prefix + trailing index when probing.
        GateCell? cell = TryResolveCell(node.Id, cellByName);
        GateCellDescriptor descriptor = cell is not null
            ? GateCellLibrary.Lookup(cell.Type)
            : GateCellDescriptor.Unknown(node.Id);

        switch (descriptor.Shape)
        {
            case GateCellShape.Gate:
                DrawGateBody(ctx, rect, descriptor);
                break;
            case GateCellShape.Inverter:
                DrawInverterBody(ctx, rect);
                break;
            case GateCellShape.Buffer:
                DrawBufferBody(ctx, rect);
                break;
            case GateCellShape.Mux:
                DrawMuxBody(ctx, rect);
                break;
            case GateCellShape.FlipFlop:
                DrawFlipFlopBody(ctx, rect);
                break;
            case GateCellShape.Latch:
                DrawLatchBody(ctx, rect);
                break;
            default:
                DrawGenericBox(ctx, rect, cell?.Type ?? node.Id);
                break;
        }
    }

    private static GateCell? TryResolveCell(string nodeId, IReadOnlyDictionary<string, GateCell> cellByName)
    {
        // Builder format: <prefix>_<sanitizedName>_<index>. The cell name
        // itself may contain underscores; we can't recover the original 1:1
        // mapping from the node id alone, so we settle for "first cell whose
        // sanitized form is a substring" — works on Yosys-generated names.
        foreach ((string name, GateCell cell) in cellByName)
        {
            if (nodeId.Contains(Sanitize(name), StringComparison.Ordinal)) return cell;
        }
        return null;
    }

    private static string Sanitize(string raw) =>
        raw.Replace('$', '_').Replace('.', '_').Replace('/', '_').Replace(':', '_')
           .Replace('[', '_').Replace(']', '_').Replace(' ', '_');

    // ── Symbol painters ───────────────────────────────────────────────────

    private static void DrawGateBody(DrawingContext ctx, Rect rect, GateCellDescriptor descriptor)
    {
        // IEEE-91 style. AND = flat-left D-shape. OR = curved chevron.
        // XOR = OR with extra inner curve. Inverted variants (NAND/NOR/XNOR)
        // overlay a small bubble on the east apex.
        GateKind? kind = descriptor.GateKind;
        bool inverted = kind is GateKind.Nand or GateKind.Nor or GateKind.Xnor;
        bool xor      = kind is GateKind.Xor or GateKind.Xnor;
        bool andLike  = kind is GateKind.And or GateKind.Nand;

        if (andLike) DrawAndShape(ctx, rect);
        else         DrawOrShape(ctx, rect, xor);

        if (inverted) DrawOutputBubble(ctx, rect);

        string label = kind?.ToString() ?? descriptor.CellType;
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, label, MutedBrush, 9);
    }

    private static void DrawAndShape(DrawingContext ctx, Rect rect)
    {
        double half = rect.Height / 2;
        double flatX = rect.X + rect.Width * 0.55;
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), true);
            gc.LineTo(new Point(flatX, rect.Y));
            gc.ArcTo(new Point(flatX, rect.Y + rect.Height),
                new Size(half, half), 0, false, SweepDirection.Clockwise);
            gc.LineTo(new Point(rect.X, rect.Y + rect.Height));
            gc.LineTo(new Point(rect.X, rect.Y));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NodeFill, NodePen, geo);
    }

    private static void DrawOrShape(DrawingContext ctx, Rect rect, bool xor)
    {
        double leftCurveDepth = rect.Width * 0.15;
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), true);
            // Top edge sweeps east to apex.
            gc.LineTo(new Point(rect.X + rect.Width * 0.4, rect.Y));
            gc.CubicBezierTo(
                new Point(rect.X + rect.Width * 0.85, rect.Y + rect.Height * 0.15),
                new Point(rect.X + rect.Width, rect.Y + rect.Height / 2 - 4),
                new Point(rect.X + rect.Width, rect.Y + rect.Height / 2));
            gc.CubicBezierTo(
                new Point(rect.X + rect.Width, rect.Y + rect.Height / 2 + 4),
                new Point(rect.X + rect.Width * 0.85, rect.Y + rect.Height * 0.85),
                new Point(rect.X + rect.Width * 0.4, rect.Y + rect.Height));
            gc.LineTo(new Point(rect.X, rect.Y + rect.Height));
            // West concave curve.
            gc.QuadraticBezierTo(
                new Point(rect.X + leftCurveDepth, rect.Y + rect.Height / 2),
                new Point(rect.X, rect.Y));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NodeFill, NodePen, geo);

        if (xor)
        {
            // Extra arc just west of the body indicates XOR.
            var arc = new StreamGeometry();
            using (StreamGeometryContext gc = arc.Open())
            {
                gc.BeginFigure(new Point(rect.X - 4, rect.Y), false);
                gc.QuadraticBezierTo(
                    new Point(rect.X + leftCurveDepth - 4, rect.Y + rect.Height / 2),
                    new Point(rect.X - 4, rect.Y + rect.Height));
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, NodePen, arc);
        }
    }

    private static void DrawOutputBubble(DrawingContext ctx, Rect rect)
    {
        const double r = 3;
        Point centre = new(rect.Right + r + 0.5, rect.Y + rect.Height / 2);
        ctx.DrawEllipse(NodeFill, NodePen, centre, r, r);
    }

    private static void DrawInverterBody(DrawingContext ctx, Rect rect)
    {
        // Triangle pointing east + output bubble.
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), true);
            gc.LineTo(new Point(rect.X + rect.Width - 6, rect.Y + rect.Height / 2));
            gc.LineTo(new Point(rect.X, rect.Y + rect.Height));
            gc.LineTo(new Point(rect.X, rect.Y));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NodeFill, NodePen, geo);
        ctx.DrawEllipse(NodeFill, NodePen, new Point(rect.X + rect.Width - 3, rect.Y + rect.Height / 2), 3, 3);
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, "NOT", MutedBrush, 9);
    }

    private static void DrawBufferBody(DrawingContext ctx, Rect rect)
    {
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), true);
            gc.LineTo(new Point(rect.X + rect.Width, rect.Y + rect.Height / 2));
            gc.LineTo(new Point(rect.X, rect.Y + rect.Height));
            gc.LineTo(new Point(rect.X, rect.Y));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NodeFill, NodePen, geo);
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, "BUF", MutedBrush, 9);
    }

    private static void DrawMuxBody(DrawingContext ctx, Rect rect)
    {
        // Trapezoid narrowing east — A / B on west, Y on east, S on south.
        var geo = new StreamGeometry();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), true);
            gc.LineTo(new Point(rect.X + rect.Width, rect.Y + rect.Height * 0.18));
            gc.LineTo(new Point(rect.X + rect.Width, rect.Y + rect.Height * 0.82));
            gc.LineTo(new Point(rect.X, rect.Y + rect.Height));
            gc.LineTo(new Point(rect.X, rect.Y));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NodeFill, NodePen, geo);
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, "MUX", MutedBrush, 9);
    }

    private static void DrawFlipFlopBody(DrawingContext ctx, Rect rect)
    {
        // Square box, west pins D / C (with chevron), east pin Q.
        ctx.DrawRectangle(NodeFill, NodePen, rect, 1, 1);
        // Clock-edge chevron near the C pin (middle-west).
        double chevX = rect.X + 1;
        double chevY = rect.Y + rect.Height * 0.6;
        var pen = new Pen(NodeStroke, 1.2);
        ctx.DrawLine(pen, new Point(chevX, chevY - 4), new Point(chevX + 5, chevY));
        ctx.DrawLine(pen, new Point(chevX, chevY + 4), new Point(chevX + 5, chevY));
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, "FF", MutedBrush, 9);
    }

    private static void DrawLatchBody(DrawingContext ctx, Rect rect)
    {
        // Rounded rectangle to distinguish from FF.
        ctx.DrawRectangle(NodeFill, NodePen, rect, 6, 6);
        DrawLabel(ctx, rect.X + 4, rect.Y - 12, "Latch", MutedBrush, 9);
    }

    private static readonly IBrush InstanceFill = SolidColorBrush.Parse("#1f2c40");
    private static readonly IBrush InstanceStroke = SolidColorBrush.Parse("#ffd166");
    private static readonly Pen InstancePen = new(InstanceStroke, 1.4);

    private static void DrawSubModuleInstance(DrawingContext ctx, ElkNode node, Rect rect)
    {
        // Outer body — Vivado uses a flat block; we add the accent border so
        // the user immediately spots "this can be expanded".
        ctx.DrawRectangle(InstanceFill, InstancePen, rect, 4, 4);

        string instanceName = node.Labels is { Count: > 0 } ? node.Labels[0].Text : node.Id;
        string moduleType   = node.Labels is { Count: > 1 } ? node.Labels[1].Text : string.Empty;

        DrawLabel(ctx, rect.X + 8, rect.Y + 4, instanceName, SolidColorBrush.Parse("#d7dde8"), 11);
        if (!string.IsNullOrEmpty(moduleType))
        {
            DrawLabel(ctx, rect.X + 8, rect.Y + 20, moduleType, MutedBrush, 9);
        }

        // "+" affordance in the top-right corner — same idea as the RTL viewer.
        const double btnSize = 14;
        Rect btn = new(rect.Right - btnSize - 6, rect.Y + 6, btnSize, btnSize);
        ctx.DrawRectangle(null, new Pen(InstanceStroke, 1), btn, 3, 3);
        DrawLabel(ctx, btn.X + 3, btn.Y - 1, "+", InstanceStroke, 11);
    }

    private static void DrawGenericBox(DrawingContext ctx, Rect rect, string label)
    {
        ctx.DrawRectangle(NodeFill, new Pen(MutedBrush, 1), rect, 1, 1);
        DrawLabel(ctx, rect.X + 4, rect.Y + rect.Height / 2 - 6, label, MutedBrush, 9);
    }

    private static void DrawLabel(DrawingContext ctx, double x, double y, string text, IBrush brush, double size)
    {
        FormattedText ft = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("monospace"),
            size,
            brush);
        ctx.DrawText(ft, new Point(x, y));
    }

    // ── Ports ─────────────────────────────────────────────────────────────

    private static void DrawPorts(DrawingContext ctx, ElkNode node)
    {
        if (node.Ports is null) return;
        foreach (ElkPort port in node.Ports)
        {
            Point centre = new(node.X + port.X, node.Y + port.Y);
            ctx.DrawEllipse(WireBrush, null, centre, 1.8, 1.8);
        }
    }

    // ── Pan + zoom + keyboard ─────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _lastPointerPos = e.GetPosition(this);
            e.Handled = true;
            return;
        }

        // Phase 6.5 Wave 2: double-click on a sub-module instance node enters
        // its scope. Single-click stays free for selection (later waves).
        if (props.IsLeftButtonPressed && e.ClickCount >= 2)
        {
            Point world = ScreenToWorld(e.GetPosition(this));
            string? instanceName = HitTestSubModule(world);
            if (instanceName is not null)
            {
                SubModuleActivated?.Invoke(this, instanceName);
                e.Handled = true;
            }
        }
    }

    private Point ScreenToWorld(Point screen) =>
        new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    private string? HitTestSubModule(Point world)
    {
        if (_graph?.Children is null) return null;
        foreach (ElkNode node in _graph.Children)
        {
            if (!node.Id.StartsWith("inst_", System.StringComparison.Ordinal)) continue;
            Rect rect = new(node.X, node.Y, node.Width, node.Height);
            if (rect.Contains(world))
            {
                return node.Labels is { Count: > 0 } ? node.Labels[0].Text : node.Id;
            }
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPanning) return;
        Point p = e.GetPosition(this);
        Vector delta = p - _lastPointerPos;
        _pan = new Point(_pan.X + delta.X, _pan.Y + delta.Y);
        _lastPointerPos = p;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y;
        if (Math.Abs(delta) < double.Epsilon) return;

        double factor = delta > 0 ? 1.15 : 1 / 1.15;
        Point pointer = e.GetPosition(this);
        double newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        // Pan correction keeps the world point under the pointer fixed.
        double zoomRatio = newZoom / _zoom;
        _pan = new Point(
            pointer.X - (pointer.X - _pan.X) * zoomRatio,
            pointer.Y - (pointer.Y - _pan.Y) * zoomRatio);
        _zoom = newZoom;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.F:
                FitToView();
                e.Handled = true;
                break;
            case Key.R:
                ResetView();
                e.Handled = true;
                break;
            case Key.OemPlus or Key.Add:
                ApplyZoomAroundCentre(1.18);
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract:
                ApplyZoomAroundCentre(1 / 1.18);
                e.Handled = true;
                break;
        }
    }

    private void ApplyZoomAroundCentre(double factor)
    {
        Point centre = new(Bounds.Width / 2, Bounds.Height / 2);
        double newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        double ratio = newZoom / _zoom;
        _pan = new Point(
            centre.X - (centre.X - _pan.X) * ratio,
            centre.Y - (centre.Y - _pan.Y) * ratio);
        _zoom = newZoom;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitPending) InvalidateVisual();
    }
}
