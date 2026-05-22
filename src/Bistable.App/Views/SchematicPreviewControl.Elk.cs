using Avalonia;
using Avalonia.Media;
using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;

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
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals)
    {
        Rect panel = ComputeElkPanelRect(bounds, moduleRect);
        _lastFocusedScopePanelRect = panel;

        DrawElkPanelChrome(context, panel);

        ElkScopeData scope = new(scopePorts, childScopes, localSignals);
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

        DrawElkEdges(context, layoutResult.Graph, transform);
        DrawElkNodes(context, layoutResult.Graph, transform);
        DrawScopeProbeSummary(context, panel, scopeSignals);
    }

    private Rect ComputeElkPanelRect(Rect bounds, Rect moduleRect)
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
            DrawElkNodeCard(context, node, rect);
        }
    }

    private void DrawElkNodeCard(DrawingContext context, ElkNode node, Rect rect)
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
            DrawElkPort(context, rect, port);
        }
    }

    private void DrawElkPort(DrawingContext context, Rect nodeRect, ElkPort port)
    {
        double px = nodeRect.X + port.X;
        double py = nodeRect.Y + port.Y;
        bool onEast = port.X >= nodeRect.Width - 1;

        Pen pinPen = new(Palette.PinStroke, 1.3);
        double stubLength = 7;
        Point pinTip = onEast ? new Point(px + stubLength, py) : new Point(px - stubLength, py);
        context.DrawLine(pinPen, new Point(px, py), pinTip);
        context.DrawEllipse(Palette.PinStroke, null, new Point(px, py), 1.6, 1.6);

        if (port.Labels is { Count: > 0 })
        {
            string label = port.Labels[0].Text;
            double labelX = onEast ? px - 6 - MeasureLabelWidth(label, 10) : px + 6;
            DrawText(context, label, labelX, py - 6, Palette.PinStroke, 9);
        }
    }

    private void DrawElkEdges(DrawingContext context, ElkGraph graph, ElkTransform transform)
    {
        foreach (ElkEdge edge in graph.Edges)
        {
            if (edge.Sections is null)
            {
                continue;
            }

            IBrush brush = ResolveElkEdgeBrush(edge);
            bool isBus = edge.Labels is { Count: > 0 } && IsBusLabel(edge.Labels[0].Text);
            double thickness = isBus ? (CompactLayout ? 2.4 : 2.8) : (CompactLayout ? 1.2 : 1.4);
            Pen pen = new(brush, thickness, lineCap: PenLineCap.Square);

            foreach (ElkEdgeSection section in edge.Sections)
            {
                Point start = transform.Apply(section.StartPoint);
                Point end = transform.Apply(section.EndPoint);
                Point previous = start;
                if (section.BendPoints is { Count: > 0 })
                {
                    foreach (ElkPoint bp in section.BendPoints)
                    {
                        Point current = transform.Apply(bp);
                        context.DrawLine(pen, previous, current);
                        previous = current;
                    }
                }

                context.DrawLine(pen, previous, end);
            }

            if (edge.JunctionPoints is { Count: > 0 })
            {
                foreach (ElkPoint jp in edge.JunctionPoints)
                {
                    Point p = transform.Apply(jp);
                    context.DrawEllipse(brush, null, p, 2.6, 2.6);
                }
            }
        }
    }

    private IBrush ResolveElkEdgeBrush(ElkEdge edge)
    {
        if (edge.Labels is { Count: > 0 } && !string.IsNullOrWhiteSpace(SelectedSignalName)
            && string.Equals(edge.Labels[0].Text, SelectedSignalName, StringComparison.OrdinalIgnoreCase))
        {
            return Palette.Selected;
        }

        return Palette.PinStroke;
    }

    private static bool IsBusLabel(string label)
    {
        // Heuristic placeholder until edges carry width metadata.
        return label.Contains("data", StringComparison.OrdinalIgnoreCase)
            || label.Contains("addr", StringComparison.OrdinalIgnoreCase)
            || label.Contains("bus", StringComparison.OrdinalIgnoreCase);
    }

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
