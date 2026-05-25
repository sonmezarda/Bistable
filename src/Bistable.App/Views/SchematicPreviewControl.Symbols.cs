using Avalonia;
using Avalonia.Media;
using Bistable.App.Services.Routing.Elk;

namespace Bistable.App.Views;

// Phase 2 symbol library: industry-standard rendering for Schematic primitives.
//
// Each Draw* method paints the body of the symbol inside the supplied node rect.
// Port-pin dots and port-pin labels are overlaid by the shared port-painter helpers
// (DrawSymbolPorts / DrawSymbolPortLabels) so the symbols stay layout-agnostic and
// match the ELK port coordinates that the layout engine returned.
//
// Conventions:
// - West side ports are inputs, East side ports are outputs (set by the builder).
// - Port.Labels[0] holds the pin glyph: "D", "Q", ">", "R" (FF/Latch); "0", "1", "S"
//   (Mux); etc.  The renderer paints those labels just *inside* the symbol body to
//   identify each pin without cluttering the wire.
public sealed partial class SchematicPreviewControl
{
    // ── D Flip-Flop (IEEE 91 style) ──────────────────────────────────────
    //
    // Rectangle body with:
    //  - D / R / Q pin labels rendered inside the body
    //  - A small ▷ (triangle) drawn inside at the clock pin to indicate edge-trigger
    //  - The QSignal name centred above the box as a title
    private void DrawElkFlipFlopNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        context.DrawRectangle(Palette.NodeFill, stroke, rect.Deflate(1));

        // Title (QSignal name) — from node.Labels[0] which is "FF q"
        if (node.Labels is { Count: > 0 })
        {
            string title = node.Labels[0].Text;
            double fontSize = Math.Clamp(rect.Height * 0.16, 7, 11);
            double textW = MeasureLabelWidth(title, fontSize);
            DrawText(context, title,
                rect.X + (rect.Width - textW) / 2,
                rect.Y - fontSize - 2,
                Palette.Text, fontSize);
        }

        // Clock-edge triangle: rendered at the inside edge of the ".clk" port
        if (node.Ports is not null)
        {
            foreach (ElkPort port in node.Ports)
            {
                if (port.Id.EndsWith(".clk", StringComparison.Ordinal))
                {
                    double px = rect.X + port.X * scale;
                    double py = rect.Y + port.Y * scale;
                    DrawClockEdgeMarker(context, px, py, stroke);
                }
            }
        }

        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // Small right-pointing triangle attached to the inside of a port — the
    // standard IEEE 91 edge-trigger glyph. Anchored so the triangle's base sits
    // exactly on the port's connection point.
    private void DrawClockEdgeMarker(DrawingContext context, double px, double py, Pen stroke)
    {
        double size = 5;
        StreamGeometry tri = new();
        using (StreamGeometryContext gc = tri.Open())
        {
            gc.BeginFigure(new Point(px, py - size), isFilled: false);
            gc.LineTo(new Point(px + size, py));
            gc.LineTo(new Point(px, py + size));
            gc.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, stroke, tri);
    }

    // ── D Latch (level-sensitive) ────────────────────────────────────────
    //
    // Rectangle body, identical to FF *except* no clock-edge triangle.
    // The G (gate) pin label is enough to communicate level-sensitivity.
    private void DrawElkLatchNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        context.DrawRectangle(Palette.NodeFill, stroke, rect.Deflate(1));

        if (node.Labels is { Count: > 0 })
        {
            string title = node.Labels[0].Text;
            double fontSize = Math.Clamp(rect.Height * 0.18, 7, 11);
            double textW = MeasureLabelWidth(title, fontSize);
            DrawText(context, title,
                rect.X + (rect.Width - textW) / 2,
                rect.Y - fontSize - 2,
                Palette.Text, fontSize);
        }

        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Mux (classic trapezoid) ──────────────────────────────────────────
    //
    // Trapezoid wider on the data-input (west) side, narrower on the output (east) side.
    // Selector(s) enter from the south side, matching Logisim/Vivado convention.
    //
    // The trapezoid is symmetric around the horizontal midline. The east side is inset
    // by ~25% of the node width to give the trapezoid silhouette.
    private void DrawElkMuxNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        double inset = rect.Height * 0.20;
        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            gc.BeginFigure(new Point(rect.X, rect.Y), isFilled: true);
            gc.LineTo(new Point(rect.Right, rect.Y + inset));
            gc.LineTo(new Point(rect.Right, rect.Bottom - inset));
            gc.LineTo(new Point(rect.X, rect.Bottom));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, geo);

        // Title above the trapezoid
        if (node.Labels is { Count: > 0 })
        {
            string title = node.Labels[0].Text;
            double fontSize = Math.Clamp(rect.Height * 0.12, 7, 11);
            double textW = MeasureLabelWidth(title, fontSize);
            DrawText(context, title,
                rect.X + (rect.Width - textW) / 2,
                rect.Y - fontSize - 2,
                Palette.Text, fontSize);
        }

        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Memory tile (RAM block) ──────────────────────────────────────────
    //
    // Tall stacked rectangle with horizontal divider lines suggesting addressable
    // cells. The cell count is decorative (up to 8 visible lines regardless of the
    // actual depth) — the precise dimensions live in the label.
    private void DrawElkMemoryNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        _ = scale; // ports are not drawn (memory has none yet); scale unused
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        Rect body = rect.Deflate(1);
        context.DrawRectangle(Palette.NodeFill, stroke, body);

        // Decorative cell-divider lines (up to 6 visible bands)
        int bands = 6;
        Pen lightStroke = new(Palette.PinStroke, 0.6);
        for (int i = 1; i < bands; i++)
        {
            double y = body.Y + body.Height * i / bands;
            context.DrawLine(lightStroke, new Point(body.X, y), new Point(body.Right, y));
        }

        // Centred label inside the body (memory tiles have no separate title above)
        if (node.Labels is { Count: > 0 })
        {
            string title = node.Labels[0].Text;
            double fontSize = Math.Clamp(body.Height * 0.13, 7, 10);
            double textW = MeasureLabelWidth(title, fontSize);
            DrawText(context, title,
                body.X + (body.Width - textW) / 2,
                body.Y + fontSize * 0.5,
                Palette.Text, fontSize);
        }
    }

    // ── Buffer (right-pointing triangle, no bubble) ──────────────────────
    //
    // Classic non-inverting buffer symbol: triangle pointing east, output coming
    // out of the apex. No output bubble (distinguishes it from the inverter).
    private void DrawElkBufferNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        DrawTriangleBody(context, rect, stroke, drawBubble: false);
        DrawSymbolTitle(context, node, rect);
        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Inverter (triangle + output bubble) ──────────────────────────────
    //
    // Same triangle as Buffer with a small circle at the apex — the classic NOT
    // gate / inverter symbol. Reuses DrawTriangleBody with drawBubble: true.
    private void DrawElkInverterNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        DrawTriangleBody(context, rect, stroke, drawBubble: true);
        DrawSymbolTitle(context, node, rect);
        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Gate (AND / OR / XOR / their N-variants) ─────────────────────────
    //
    // Dispatches to the existing IEEE 91 gate body painters (DrawAndGate / DrawOrGate)
    // based on the GateKind embedded in the node label as the first whitespace-separated
    // token. The N-variants (Nand/Nor/Xnor) draw the matching base shape plus an output
    // bubble that overlays the symbol's east apex.
    private void DrawElkGateNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        string kind = ParseFirstToken(node);
        bool inverted = kind is "Nand" or "Nor" or "Xnor";

        switch (kind)
        {
            case "And" or "Nand" or "ReduceAnd":
                DrawAndGate(context, rect, stroke);
                break;
            case "Or" or "Nor" or "ReduceOr":
                DrawOrGate(context, rect, stroke, xor: false);
                break;
            case "Xor" or "Xnor" or "ReduceXor":
                DrawOrGate(context, rect, stroke, xor: true);
                break;
            default:
                // Unknown gate kind — render the generic operator box so the user
                // still sees something instead of a silent miss.
                DrawOperatorBox(context, rect, kind, stroke);
                break;
        }

        if (inverted)
        {
            double bubbleR = Math.Min(rect.Height, rect.Width) * 0.08;
            double cx = rect.Right + bubbleR;
            double cy = rect.Y + rect.Height * 0.5;
            context.DrawEllipse(Palette.NodeFill, stroke, new Point(cx, cy), bubbleR, bubbleR);
        }

        DrawSymbolTitle(context, node, rect);
        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Arithmetic / comparison block ────────────────────────────────────
    //
    // Rectangle with an operator glyph centred inside (e.g. "+", "−", "×", "÷", "=", "<").
    // Distinct from logic gates so the reader can quickly tell datapath from control logic.
    private void DrawElkArithNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        context.DrawRectangle(Palette.NodeFill, stroke, rect.Deflate(1));

        string kind = ParseFirstToken(node);
        string glyph = ArithGlyph(kind);
        if (!string.IsNullOrEmpty(glyph))
        {
            double fontSize = Math.Clamp(rect.Height * 0.5, 9, 16);
            double textW = MeasureLabelWidth(glyph, fontSize);
            DrawText(context, glyph,
                rect.X + (rect.Width - textW) / 2,
                rect.Y + (rect.Height - fontSize) / 2 - fontSize * 0.1,
                Palette.Text, fontSize);
        }

        DrawSymbolTitle(context, node, rect);
        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    private static string ArithGlyph(string kind) => kind switch
    {
        "Add" => "+",
        "Sub" => "−",
        "Mul" => "×",
        "Div" => "÷",
        "Mod" => "%",
        "ShiftLeft"            => "<<",
        "ShiftRight"           => ">>",
        "ShiftRightArithmetic" => ">>>",
        "Equal"                => "=",
        "NotEqual"             => "≠",
        "LessThan"             => "<",
        "GreaterThan"          => ">",
        "LessOrEqual"          => "≤",
        "GreaterOrEqual"       => "≥",
        _ => kind   // fall back to the raw kind for forward compatibility
    };

    // Triangle body shared by Buffer and Inverter. Apex points east at the centre-
    // line; if drawBubble is true, a small unfilled circle is drawn at the apex tip.
    private void DrawTriangleBody(DrawingContext context, Rect r, Pen stroke, bool drawBubble)
    {
        double bubbleR = drawBubble ? Math.Min(r.Width, r.Height) * 0.12 : 0;
        double tipX = r.Right - (drawBubble ? bubbleR * 2 : 0);
        double midY = r.Y + r.Height * 0.5;

        StreamGeometry tri = new();
        using (StreamGeometryContext gc = tri.Open())
        {
            gc.BeginFigure(new Point(r.X, r.Y), isFilled: true);
            gc.LineTo(new Point(tipX, midY));
            gc.LineTo(new Point(r.X, r.Bottom));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, tri);

        if (drawBubble)
        {
            context.DrawEllipse(Palette.NodeFill, stroke,
                new Point(tipX + bubbleR, midY), bubbleR, bubbleR);
        }
    }

    // First whitespace-separated token from the node's primary label.
    // Used by Gate and Arith renderers to discover their kind without an enum
    // round-trip through the ElkGraph data model.
    private static string ParseFirstToken(ElkNode node)
    {
        if (node.Labels is not { Count: > 0 }) return string.Empty;
        string text = node.Labels[0].Text;
        int space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    // Render the node's primary label above the symbol body (used by every symbol
    // type) so the output signal name reads as a title.
    private void DrawSymbolTitle(DrawingContext context, ElkNode node, Rect rect)
    {
        if (node.Labels is not { Count: > 0 }) return;
        string title = node.Labels[0].Text;
        double fontSize = Math.Clamp(rect.Height * 0.16, 7, 11);
        double textW = MeasureLabelWidth(title, fontSize);
        DrawText(context, title,
            rect.X + (rect.Width - textW) / 2,
            rect.Y - fontSize - 2,
            Palette.Text, fontSize);
    }

    // ── Struct fan-out (P2-11): inverse splitter wedge ───────────────────
    //
    // Mirrors the existing splitter wedge but reversed: single west input apex,
    // wide east face holding N labelled output ports — one per packed-struct field.
    // The struct's qualified type name (e.g. control_pkg::ctrl_t) renders above
    // the wedge; per-field labels (port.Labels[0]) render inside the wedge body,
    // right-aligned next to each east port.
    private void DrawElkStructFanOutNode(DrawingContext context, ElkNode node, Rect rect, double scale)
    {
        Pen stroke = new(Palette.ModuleStroke, 1.5);
        double midY = rect.Y + rect.Height / 2;
        double indentX = Math.Min(rect.Width * 0.32, 12 * scale);

        StreamGeometry geo = new();
        using (StreamGeometryContext gc = geo.Open())
        {
            // Apex on the west (single input); flat right edge (N outputs).
            gc.BeginFigure(new Point(rect.X, midY), isFilled: true);
            gc.LineTo(new Point(rect.X + indentX, rect.Y));
            gc.LineTo(new Point(rect.Right, rect.Y));
            gc.LineTo(new Point(rect.Right, rect.Bottom));
            gc.LineTo(new Point(rect.X + indentX, rect.Bottom));
            gc.EndFigure(isClosed: true);
        }
        context.DrawGeometry(Palette.NodeFill, stroke, geo);

        DrawSymbolTitle(context, node, rect);
        DrawSymbolPortsAndLabels(context, node, rect, scale, stroke);
    }

    // ── Shared port painter ──────────────────────────────────────────────
    //
    // Draws each port as a small filled dot at its connection coordinate and
    // paints the port label just inside or just outside the symbol body depending
    // on side. West-side labels are left of the pin, East-side labels right of the
    // pin, and South-side selector labels sit below the pin.
    private void DrawSymbolPortsAndLabels(DrawingContext context, ElkNode node, Rect rect, double scale, Pen stroke)
    {
        _ = stroke;
        if (node.Ports is null) return;

        double labelFont = Math.Clamp(8 * scale, 7, 10);
        double labelInset = 4 * scale;

        foreach (ElkPort port in node.Ports)
        {
            double px = rect.X + port.X * scale;
            double py = rect.Y + port.Y * scale;
            context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 2.2, 2.2);

            if (port.Labels is not { Count: > 0 }) continue;
            string label = port.Labels[0].Text;
            if (ShouldHideInlinePortLabel(port, node, label))
                continue;

            double textW = MeasureLabelWidth(label, labelFont);

            bool onEast = port.X >= node.Width - 1;
            bool onSouth = port.Y >= node.Height - 1;
            double labelX = onSouth
                ? px - textW / 2
                : onEast
                    ? px - labelInset - textW
                    : px + labelInset;
            double labelY = onSouth ? py + labelFont * 0.25 : py - labelFont * 0.6;
            DrawText(context, label, labelX, labelY, Palette.PinStroke, labelFont);
        }
    }

    private static bool ShouldHideInlinePortLabel(ElkPort port, ElkNode node, string label)
    {
        // South-side mux selector labels quickly collide with the first wire bend.
        // The label remains in the ELK model for selection/details; only the inline
        // paint is suppressed.
        if (port.Y >= node.Height - 1)
            return true;

        return false;
    }
}
