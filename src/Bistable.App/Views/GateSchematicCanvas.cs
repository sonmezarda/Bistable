using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Design.Schematic;
using Bistable.Core.Projects;
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
    private const double MinZoom = 0.005;
    private const double MaxZoom = 8.0;
    private const double FitPadding = 24;
    private static readonly IBrush CanvasBackground = SolidColorBrush.Parse("#0e141c");

    private ElkGraph? _graph;
    private GateModule? _module;
    private IReadOnlyDictionary<string, GateCell> _cellByName =
        new Dictionary<string, GateCell>(StringComparer.Ordinal);
    private ElkEdgeCoordinateContext? _edgeCoordinateContext;
    private double _zoom = 1.0;
    private Point _pan;
    private Point _lastPointerPos;
    private bool _isPanning;
    private bool _fitPending = true;
    private int? _highlightedNetId;
    private string? _selectedCellName;
    private string? _highlightedBundleId;
    private IReadOnlyDictionary<string, GateBusBundle> _bundlesById =
        new Dictionary<string, GateBusBundle>(StringComparer.Ordinal);
    private GatePinLabelDisplayOptions _pinLabelOptions = GatePinLabelDisplayOptions.Default;

    /// <summary>
    /// Phase 6.5 Wave 2: raised when the user double-clicks (or single-clicks
    /// the "+" badge on) a sub-module instance. The event argument is the
    /// instance name; the window translates it into a breadcrumb push.
    /// </summary>
    public event EventHandler<string>? SubModuleActivated;

    public event EventHandler<string>? SubModuleExpansionToggled;

    /// <summary>
    /// Phase 6.5 Wave 3: raised when the user clicks a gate-level wire or pin.
    /// The payload identifies the Yosys net id and, when available, a user
    /// netname from the current module's netname table.
    /// </summary>
    public event EventHandler<GateNetSelection?>? NetSelected;

    public event EventHandler<GateCellSelection?>? CellSelected;

    /// <summary>
    /// Phase 6.5 follow-up: raised when the user clicks an edge that is part of
    /// a bus bundle. Listeners can show the bundle's logical name + `[msb:lsb]`
    /// in a properties panel without parsing edge labels themselves.
    /// </summary>
    public event EventHandler<GateBusBundleSelection?>? BundleSelected;

    public GateSchematicCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Replace the rendered graph and force a re-fit on next draw.</summary>
    public void SetGraph(
        ElkGraph graph,
        GateModule module,
        IReadOnlyList<GateBusBundle>? bundles = null)
    {
        _graph = graph;
        _module = module;
        _cellByName = module.Cells.ToDictionary(static cell => cell.Name, StringComparer.Ordinal);
        _edgeCoordinateContext = BuildEdgeCoordinateContext(graph);
        _bundlesById = bundles is null
            ? new Dictionary<string, GateBusBundle>(StringComparer.Ordinal)
            : bundles.ToDictionary(static b => b.Id, StringComparer.Ordinal);
        _highlightedNetId = null;
        _highlightedBundleId = null;
        _selectedCellName = null;
        _fitPending = true;
        InvalidateVisual();
    }

    public void HighlightBundle(string? bundleId)
    {
        _highlightedBundleId = bundleId is not null && _bundlesById.ContainsKey(bundleId)
            ? bundleId
            : null;
        InvalidateVisual();
    }

    public void HighlightNet(int? netId)
    {
        _highlightedNetId = netId;
        InvalidateVisual();
    }

    public void SelectCell(string? cellName)
    {
        _selectedCellName = cellName;
        InvalidateVisual();
    }

    public void CenterOnCell(string cellName)
    {
        if (_graph?.Children is null) return;
        foreach ((ElkNode node, double absX, double absY) in EnumerateNodes(_graph.Children))
        {
            if (NodeRepresentsCell(node, cellName))
            {
                CenterOnWorldPoint(new Point(absX + node.Width / 2, absY + node.Height / 2));
                return;
            }
        }
    }

    public void CenterOnNet(int netId)
    {
        if (_graph?.Edges is null) return;
        ElkEdgeCoordinateContext coordinateContext =
            _edgeCoordinateContext ??= BuildEdgeCoordinateContext(_graph);
        foreach (ElkEdge edge in _graph.Edges)
        {
            if (TryGetEdgeNetId(edge) != netId || edge.Sections is not { Count: > 0 }) continue;
            ElkEdgeSection section = edge.Sections[0];
            Point offset = ResolveEdgeCoordinateOffset(edge, coordinateContext);
            Point start = OffsetPoint(section.StartPoint, offset);
            Point end = section.BendPoints is { Count: > 0 } bends
                ? OffsetPoint(bends[0], offset)
                : OffsetPoint(section.EndPoint, offset);
            CenterOnWorldPoint(new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2));
            return;
        }
    }

    private void CenterOnWorldPoint(Point world)
    {
        _fitPending = false;
        _pan = new Point(
            Bounds.Width / 2 - world.X * _zoom,
            Bounds.Height / 2 - world.Y * _zoom);
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

    /// <summary>
    /// Moves the viewport without rebuilding or re-laying out the graph.
    /// Used by dock/session restoration and performance regression coverage.
    /// Pointer panning delegates to the same state update.
    /// </summary>
    public void PanBy(Vector delta)
    {
        _fitPending = false;
        _pan = new Point(_pan.X + delta.X, _pan.Y + delta.Y);
        InvalidateVisual();
    }

    public void SetPinLabelOptions(GatePinLabelDisplayOptions options)
    {
        _pinLabelOptions = options.Normalize();
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
            // Render order matters: cell bodies first (so leaf gate symbols
            // erase anything beneath them), then wires (so they sit on top of
            // any compound/group backgrounds and aren't hidden by them), then
            // port dots + selection highlight on the very top.
            bool overview = _zoom < 0.55;
            DrawNodes(
                context,
                _graph,
                _selectedCellName,
                RenderPass.Bodies,
                overview,
                1.0 / Math.Max(_zoom, MinZoom),
                _zoom,
                _pinLabelOptions);
            DrawEdges(context, _graph, _edgeCoordinateContext!, overview);
            DrawNodes(
                context,
                _graph,
                _selectedCellName,
                RenderPass.Overlays,
                overview,
                1.0 / Math.Max(_zoom, MinZoom),
                _zoom,
                _pinLabelOptions);
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
        _zoom = CalculateFitZoom(Bounds.Size, new Size(gw, gh));
        double renderedW = gw * _zoom;
        double renderedH = gh * _zoom;
        _pan = new Point(
            Math.Max(FitPadding, (Bounds.Width - renderedW) / 2),
            Math.Max(FitPadding, (Bounds.Height - renderedH) / 2));
        _fitPending = false;
    }

    internal static double CalculateFitZoom(Size viewport, Size graph)
    {
        double availW = Math.Max(1, viewport.Width - FitPadding * 2);
        double availH = Math.Max(1, viewport.Height - FitPadding * 2);
        double graphWidth = Math.Max(1, graph.Width);
        double graphHeight = Math.Max(1, graph.Height);
        return Math.Clamp(Math.Min(availW / graphWidth, availH / graphHeight), MinZoom, MaxZoom);
    }

    // ── Edges ─────────────────────────────────────────────────────────────

    private static readonly IBrush WireBrush = SolidColorBrush.Parse("#65d889");
    private static readonly IBrush OverviewWireBrush =
        new SolidColorBrush(Color.FromArgb(120, 101, 216, 137));
    private static readonly IBrush HighlightWireBrush = SolidColorBrush.Parse("#ffd166");
    private static readonly Pen WirePen = new(WireBrush, 1.0);
    private static readonly Pen HighlightWirePen = new(HighlightWireBrush, 2.6);

    // Bundle trunk overlay: at overview zoom and below the detailed threshold
    // we don't draw individual bit edges in a different colour, but we DO
    // thicken bundle members so the user can see "this is a bus, not one
    // scalar wire". Highlighted bundles win over net highlight so clicking a
    // single bit also lights up the whole bus it belongs to.
    private static readonly Pen BundleTrunkPen =
        new(SolidColorBrush.Parse("#65d889"), 2.4);
    private static readonly Pen BundleHighlightPen =
        new(SolidColorBrush.Parse("#ffd166"), 3.4);

    private void DrawEdges(
        DrawingContext context,
        ElkGraph graph,
        ElkEdgeCoordinateContext coordinateContext,
        bool overview)
    {
        if (graph.Edges is null) return;
        Pen overviewPen = new(OverviewWireBrush, 0.65 / Math.Max(_zoom, MinZoom));
        foreach (ElkEdge edge in graph.Edges)
        {
            if (edge.Sections is null) continue;
            Pen pen = ResolveEdgePen(edge, overview, overviewPen);
            Point offset = ResolveEdgeCoordinateOffset(edge, coordinateContext);
            foreach (ElkEdgeSection section in edge.Sections)
            {
                Point prev = OffsetPoint(section.StartPoint, offset);
                if (section.BendPoints is { } bends)
                {
                    foreach (ElkPoint bp in bends)
                    {
                        Point cur = OffsetPoint(bp, offset);
                        context.DrawLine(pen, prev, cur);
                        prev = cur;
                    }
                }
                context.DrawLine(pen, prev, OffsetPoint(section.EndPoint, offset));
            }
        }
    }

    private Pen ResolveEdgePen(ElkEdge edge, bool overview, Pen overviewPen)
    {
        string? bundleId = TryGetEdgeBundleId(edge);
        if (bundleId is not null && bundleId == _highlightedBundleId)
        {
            return BundleHighlightPen;
        }
        if (_highlightedNetId is { } netId && TryGetEdgeNetId(edge) == netId)
        {
            return HighlightWirePen;
        }
        if (bundleId is not null && !overview)
        {
            // Compact LOD: emphasise bus members so the user can tell a wide
            // bus apart from a stack of unrelated parallel scalars.
            return BundleTrunkPen;
        }
        return overview ? overviewPen : WirePen;
    }

    private static string? TryGetEdgeBundleId(ElkEdge edge)
    {
        if (edge.LayoutOptions is null) return null;
        return edge.LayoutOptions.TryGetValue(
                GateBusBundleKeys.BundleIdLayoutOption, out string? id)
            ? id
            : null;
    }

    // ── Nodes ─────────────────────────────────────────────────────────────

    // GateNetlistElkBuilder tags sub-module instance nodes with this id prefix.
    private const string SubModuleIdPrefix = "inst_";

    private static readonly IBrush NodeStroke = SolidColorBrush.Parse("#5dbcff");
    private static readonly IBrush NodeFill   = SolidColorBrush.Parse("#1b2230");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush PinLabelBackground =
        new SolidColorBrush(Color.FromArgb(215, 14, 20, 28));
    private static readonly Pen NodePen = new(NodeStroke, 1.2);

    private enum RenderPass { Bodies, Overlays }

    private static void DrawNodes(
        DrawingContext context,
        ElkGraph graph,
        string? selectedCellName,
        RenderPass pass,
        bool overview,
        double inverseZoom,
        double zoom,
        GatePinLabelDisplayOptions pinLabelOptions)
    {
        if (graph.Children is null) return;
        DrawNodesRecursive(
            context,
            graph.Children,
            selectedCellName,
            baseX: 0,
            baseY: 0,
            pass,
            overview,
            inverseZoom,
            zoom,
            pinLabelOptions);
    }

    private static void DrawNodesRecursive(
        DrawingContext context,
        IReadOnlyList<ElkNode> nodes,
        string? selectedCellName,
        double baseX,
        double baseY,
        RenderPass pass,
        bool overview,
        double inverseZoom,
        double zoom,
        GatePinLabelDisplayOptions pinLabelOptions)
    {
        foreach (ElkNode node in nodes)
        {
            double absX = baseX + node.X;
            double absY = baseY + node.Y;
            Rect rect = new(absX, absY, node.Width, node.Height);
            if (pass == RenderPass.Bodies)
            {
                DrawNodeBackground(context, node, rect);
                if (overview)
                {
                    DrawNodeOverview(context, node, rect, inverseZoom);
                }
                else
                {
                    DrawNodeForeground(context, node, rect);
                }
            }
            else
            {
                if (selectedCellName is not null && NodeRepresentsCell(node, selectedCellName))
                {
                    context.DrawRectangle(null, new Pen(HighlightWireBrush, 2.2), rect.Inflate(4), 4, 4);
                }
                DrawPorts(
                    context,
                    node,
                    absX,
                    absY,
                    zoom,
                    pinLabelOptions,
                    drawDots: !overview);
            }
            if (node.Children is { Count: > 0 })
            {
                DrawNodesRecursive(
                    context,
                    node.Children,
                    selectedCellName,
                    absX,
                    absY,
                    pass,
                    overview,
                    inverseZoom,
                    zoom,
                    pinLabelOptions);
            }
        }
    }

    private static void DrawNodeOverview(
        DrawingContext context,
        ElkNode node,
        Rect rect,
        double inverseZoom)
    {
        if (node.Id is "boundary_in" or "boundary_out")
        {
            return;
        }

        context.DrawRectangle(null, new Pen(NodeStroke, inverseZoom), rect, 2, 2);
    }

    private static bool NodeRepresentsCell(ElkNode node, string cellName)
    {
        if (node.Labels is { Count: > 2 })
        {
            string labelName = node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal)
                ? node.Labels[0].Text
                : node.Labels[2].Text;
            return string.Equals(labelName, cellName, StringComparison.Ordinal);
        }
        return false;
    }

    private static void DrawNodeBackground(DrawingContext ctx, ElkNode node, Rect rect)
    {
        if (node.Id is "boundary_in" or "boundary_out")
        {
            // Boundary anchors get a faint surround so the user can see them.
            ctx.DrawRectangle(NodeFill, new Pen(MutedBrush, 0.6) { DashStyle = DashStyle.Dash }, rect, 2, 2);
            return;
        }

        // Expanded sub-module compounds (inst_ with children) deliberately
        // skip the body fill: child cells live inside, and the inter-cell
        // wires routed through this rect would otherwise be hidden under it.
        // DrawSubModuleInstance still draws the border + header in
        // DrawNodeForeground.
        if (node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal) && node.Children is { Count: > 0 })
        {
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
        Rect rect)
    {
        if (node.Id is "boundary_in" or "boundary_out")
        {
            string label = node.Id == "boundary_in" ? "IN" : "OUT";
            DrawLabel(ctx, rect.X + 6, rect.Y + 4, label, MutedBrush, 9);
            return;
        }

        // Phase 6.5 Wave 2: sub-module instance — draw as Vivado-style
        // expandable block with instance name + module-type chip + "+" hint.
        if (node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal))
        {
            DrawSubModuleInstance(ctx, node, rect);
            return;
        }

        // Find the original cell for this node — the builder's prefixes are
        // gate_/ff_/mux_/inv_/buf_/latch_/node_ followed by sanitized cell
        // name + cell index. We strip the prefix + trailing index when probing.
        GateCellDescriptor descriptor = ResolveDescriptorFromLabels(node);

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
                DrawGenericBox(ctx, rect, descriptor.CellType);
                break;
        }
    }

    internal static GateCell? TryResolveCell(
        ElkNode node,
        IReadOnlyDictionary<string, GateCell> cellByName)
    {
        // labels[2] is the builder's exact source cell name. Never infer the
        // cell from a prefixed node id: nested ids contain their parent
        // instance path (for example u_alu__), so substring matching turns
        // every child primitive into the parent module instance.
        if (node.Labels is not { Count: > 2 })
        {
            return null;
        }

        return cellByName.TryGetValue(node.Labels[2].Text, out GateCell? cell)
            ? cell
            : null;
    }

    internal static GateCellDescriptor ResolveDescriptorFromLabels(ElkNode node)
    {
        if (node.Labels is { Count: > 1 } && !string.IsNullOrWhiteSpace(node.Labels[1].Text))
        {
            return GateCellLibrary.Lookup(node.Labels[1].Text);
        }
        return GateCellDescriptor.Unknown(node.Id);
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
        // Collapsed: filled block so it reads as a closed unit.
        // Expanded (has children): border-only so the interior reads as a
        // group container and child cells / wires stay visible underneath.
        bool expanded = node.Children is { Count: > 0 };
        IBrush? fill = expanded ? null : InstanceFill;
        ctx.DrawRectangle(fill, InstancePen, rect, 4, 4);

        string instanceName = node.Labels is { Count: > 0 } ? node.Labels[0].Text : node.Id;
        string moduleType   = node.Labels is { Count: > 1 } ? node.Labels[1].Text : string.Empty;

        DrawLabel(ctx, rect.X + 8, rect.Y + 4, instanceName, SolidColorBrush.Parse("#d7dde8"), 11);
        if (!string.IsNullOrEmpty(moduleType))
        {
            DrawLabel(ctx, rect.X + 8, rect.Y + 20, moduleType, MutedBrush, 9);
        }

        // Expand affordance in the top-right corner — same idea as the RTL viewer.
        Rect btn = SubModuleButtonRect(rect);
        ctx.DrawRectangle(null, new Pen(InstanceStroke, 1), btn, 3, 3);
        string glyph = expanded ? "-" : "+";
        DrawLabel(ctx, btn.X + 3, btn.Y - 1, glyph, InstanceStroke, 11);
    }

    private static Rect SubModuleButtonRect(Rect rect)
    {
        const double btnSize = 14;
        return new Rect(rect.Right - btnSize - 6, rect.Y + 6, btnSize, btnSize);
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

    private static void DrawPorts(
        DrawingContext ctx,
        ElkNode node,
        double nodeAbsoluteX,
        double nodeAbsoluteY,
        double zoom,
        GatePinLabelDisplayOptions pinLabelOptions,
        bool drawDots)
    {
        if (node.Ports is null) return;
        if (drawDots)
        {
            foreach (ElkPort port in node.Ports)
            {
                Point centre = new(nodeAbsoluteX + port.X, nodeAbsoluteY + port.Y);
                ctx.DrawEllipse(WireBrush, null, centre, 1.8, 1.8);
            }
        }

        foreach (GatePinLabel label in GatePinLabelLayout.Resolve(node.Ports, zoom, pinLabelOptions))
        {
            Point centre = new(
                nodeAbsoluteX + label.Port.X,
                nodeAbsoluteY + label.Port.Y);
            FormattedText text = CreatePinLabelText(label.Text);
            double x = label.IsWestSide
                ? centre.X + 5
                : centre.X - 5 - text.Width;
            double y = centre.Y - text.Height / 2;
            ctx.FillRectangle(
                PinLabelBackground,
                new Rect(x - 2, y - 1, text.Width + 4, text.Height + 2),
                2);
            ctx.DrawText(text, new Point(x, y));
        }
    }

    private static FormattedText CreatePinLabelText(string text) =>
        new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("monospace"),
            8.5,
            SolidColorBrush.Parse("#b7c4d8"));

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
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            Point world = ScreenToWorld(e.GetPosition(this));
            string? badgeInstanceName = HitTestSubModuleButton(world);
            if (badgeInstanceName is not null)
            {
                SubModuleExpansionToggled?.Invoke(this, badgeInstanceName);
                e.Handled = true;
                return;
            }

            NetHit? hit = HitTestNet(world);
            if (hit is { } netHit)
            {
                HighlightNet(netHit.NetId);
                HighlightBundle(netHit.BundleId);
                SelectCell(null);
                NetSelected?.Invoke(this, new GateNetSelection(netHit.NetId, ResolveNetName(netHit.NetId)));
                CellSelected?.Invoke(this, null);
                BundleSelected?.Invoke(this, netHit.BundleId is { } bid
                    && _bundlesById.TryGetValue(bid, out GateBusBundle? bundle)
                    ? new GateBusBundleSelection(bundle)
                    : null);
            }
            else if (HitTestCell(world) is { } cell)
            {
                HighlightNet(null);
                HighlightBundle(null);
                SelectCell(cell.Name);
                NetSelected?.Invoke(this, null);
                CellSelected?.Invoke(this, new GateCellSelection(cell));
                BundleSelected?.Invoke(this, null);
            }
            else if (_highlightedNetId is not null || _selectedCellName is not null || _highlightedBundleId is not null)
            {
                HighlightNet(null);
                HighlightBundle(null);
                SelectCell(null);
                NetSelected?.Invoke(this, null);
                CellSelected?.Invoke(this, null);
                BundleSelected?.Invoke(this, null);
            }
            e.Handled = true;
        }
    }

    private Point ScreenToWorld(Point screen) =>
        new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    private string? HitTestSubModule(Point world)
    {
        if (_graph?.Children is null) return null;
        return HitTestSubModuleRecursive(_graph.Children, world, baseX: 0, baseY: 0);
    }

    private string? HitTestSubModuleRecursive(IReadOnlyList<ElkNode> nodes, Point world, double baseX, double baseY)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            ElkNode node = nodes[i];
            double absX = baseX + node.X;
            double absY = baseY + node.Y;
            if (node.Children is { Count: > 0 }
                && HitTestSubModuleRecursive(node.Children, world, absX, absY) is { } childHit)
            {
                return childHit;
            }

            if (!node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal)) continue;
            Rect rect = new(absX, absY, node.Width, node.Height);
            if (rect.Contains(world))
            {
                return GetInstancePath(node);
            }
        }
        return null;
    }

    private string? HitTestSubModuleButton(Point world)
    {
        if (_graph?.Children is null) return null;
        return HitTestSubModuleButtonRecursive(_graph.Children, world, baseX: 0, baseY: 0);
    }

    private string? HitTestSubModuleButtonRecursive(IReadOnlyList<ElkNode> nodes, Point world, double baseX, double baseY)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            ElkNode node = nodes[i];
            double absX = baseX + node.X;
            double absY = baseY + node.Y;
            if (node.Children is { Count: > 0 }
                && HitTestSubModuleButtonRecursive(node.Children, world, absX, absY) is { } childHit)
            {
                return childHit;
            }

            if (!node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal)) continue;
            Rect rect = new(absX, absY, node.Width, node.Height);
            if (SubModuleButtonRect(rect).Contains(world))
            {
                return GetInstancePath(node);
            }
        }
        return null;
    }

    private static string GetInstancePath(ElkNode node)
    {
        if (node.Labels is { Count: > 2 } && !string.IsNullOrWhiteSpace(node.Labels[2].Text))
        {
            return node.Labels[2].Text;
        }
        return node.Labels is { Count: > 0 } ? node.Labels[0].Text : node.Id;
    }

    private readonly record struct NetHit(int NetId, string? BundleId);

    private NetHit? HitTestNet(Point world)
    {
        if (_graph is null) return null;

        double tolerance = Math.Max(4.0, 7.0 / Math.Max(_zoom, MinZoom));
        if (_graph.Children is { } children)
        {
            foreach ((ElkNode node, double absX, double absY) in EnumerateNodes(children))
            {
                if (node.Ports is null) continue;
                foreach (ElkPort port in node.Ports)
                {
                    Point centre = new(absX + port.X, absY + port.Y);
                    if (Distance(world, centre) <= tolerance
                        && TryFindNetForPort(port.Id) is { } portNet)
                    {
                        // Port-dot hits don't disclose which member edge the
                        // user meant, so leave the bundle field null — the
                        // edge-segment branch below resolves it accurately.
                        return new NetHit(portNet, BundleId: null);
                    }
                }
            }
        }

        if (_graph.Edges is null) return null;
        ElkEdgeCoordinateContext coordinateContext =
            _edgeCoordinateContext ??= BuildEdgeCoordinateContext(_graph);
        foreach (ElkEdge edge in _graph.Edges)
        {
            int? netId = TryGetEdgeNetId(edge);
            if (netId is null || edge.Sections is null) continue;
            Point offset = ResolveEdgeCoordinateOffset(edge, coordinateContext);
            foreach (ElkEdgeSection section in edge.Sections)
            {
                Point prev = OffsetPoint(section.StartPoint, offset);
                if (section.BendPoints is { } bends)
                {
                    foreach (ElkPoint bp in bends)
                    {
                        Point cur = OffsetPoint(bp, offset);
                        if (DistanceToSegment(world, prev, cur) <= tolerance)
                            return new NetHit(netId.Value, TryGetEdgeBundleId(edge));
                        prev = cur;
                    }
                }
                Point end = OffsetPoint(section.EndPoint, offset);
                if (DistanceToSegment(world, prev, end) <= tolerance)
                    return new NetHit(netId.Value, TryGetEdgeBundleId(edge));
            }
        }
        return null;
    }

    private GateCell? HitTestCell(Point world)
    {
        if (_graph?.Children is null || _module is null) return null;
        foreach ((ElkNode node, double absX, double absY) in EnumerateNodes(_graph.Children))
        {
            if (node.Id is "boundary_in" or "boundary_out") continue;
            Rect rect = new(absX, absY, node.Width, node.Height);
            if (!rect.Contains(world)) continue;

            if (node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal)
                && node.Labels is { Count: > 0 })
            {
                string instanceName = node.Labels[0].Text;
                return _module.Cells.FirstOrDefault(c => string.Equals(c.Name, instanceName, StringComparison.Ordinal))
                    ?? BuildTransientCellFromLabels(node);
            }

            if (TryResolveCell(node, _cellByName) is { } sourceCell)
            {
                return sourceCell;
            }
            if (BuildTransientCellFromLabels(node) is { } transient)
            {
                return transient;
            }
        }
        return null;
    }

    private static GateCell? BuildTransientCellFromLabels(ElkNode node)
    {
        if (node.Labels is not { Count: > 2 }) return null;
        string type = node.Labels[1].Text;
        string name = node.Id.StartsWith(SubModuleIdPrefix, StringComparison.Ordinal)
            ? node.Labels[0].Text
            : node.Labels[2].Text;
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(name)) return null;
        return new GateCell(
            name,
            type,
            new Dictionary<string, GateConnection>(),
            new Dictionary<string, GatePortDirection>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
    }

    private int? TryFindNetForPort(string portId)
    {
        if (_graph?.Edges is null) return null;
        foreach (ElkEdge edge in _graph.Edges)
        {
            if ((edge.Sources?.Contains(portId) ?? false)
                || (edge.Targets?.Contains(portId) ?? false))
            {
                return TryGetEdgeNetId(edge);
            }
        }
        return null;
    }

    private string? ResolveNetName(int netId)
    {
        if (_module?.Nets is null) return null;
        foreach (GateNet net in _module.Nets)
        {
            if (net.Bits.Any(bit => bit.Kind == BitKind.Net && bit.NetId == netId))
            {
                return net.Name;
            }
        }
        return null;
    }

    private static int? TryGetEdgeNetId(ElkEdge edge)
    {
        if (edge.Labels is null) return null;
        foreach (ElkLabel label in edge.Labels)
        {
            string text = label.Text.Trim();
            if (text.StartsWith("net", StringComparison.Ordinal)
                && int.TryParse(text[3..], out int netId))
            {
                return netId;
            }
        }
        return null;
    }

    private static IEnumerable<(ElkNode Node, double AbsoluteX, double AbsoluteY)> EnumerateNodes(
        IReadOnlyList<ElkNode> nodes)
    {
        foreach (ElkNode node in nodes)
        {
            foreach (var item in EnumerateNodes(node, baseX: 0, baseY: 0))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(ElkNode Node, double AbsoluteX, double AbsoluteY)> EnumerateNodes(
        ElkNode node,
        double baseX,
        double baseY)
    {
        double absX = baseX + node.X;
        double absY = baseY + node.Y;
        yield return (node, absX, absY);
        if (node.Children is null) yield break;
        foreach (ElkNode child in node.Children)
        {
            foreach (var item in EnumerateNodes(child, absX, absY))
            {
                yield return item;
            }
        }
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

    private static Point OffsetPoint(ElkPoint point, Point offset) =>
        new(point.X + offset.X, point.Y + offset.Y);

    private sealed record ElkEdgeCoordinateContext(
        IReadOnlyDictionary<string, string[]> EndpointPaths,
        IReadOnlyDictionary<string, Point> NodeOrigins);

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return Distance(p, a);
        }

        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        Point projection = new(a.X + t * dx, a.Y + t * dy);
        return Distance(p, projection);
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

public sealed record GateNetSelection(int NetId, string? NetName);

public sealed record GateCellSelection(GateCell Cell);
