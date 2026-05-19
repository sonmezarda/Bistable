using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed class HierarchyGraphControl : Control
{
    public static readonly StyledProperty<HierarchyNodeViewModel?> RootProperty =
        AvaloniaProperty.Register<HierarchyGraphControl, HierarchyNodeViewModel?>(nameof(Root));

    public static readonly StyledProperty<string?> SelectedPathProperty =
        AvaloniaProperty.Register<HierarchyGraphControl, string?>(nameof(SelectedPath));

    public static readonly StyledProperty<IEnumerable<HierarchyTraceScopeSummaryViewModel>?> ScopeSummariesProperty =
        AvaloniaProperty.Register<HierarchyGraphControl, IEnumerable<HierarchyTraceScopeSummaryViewModel>?>(nameof(ScopeSummaries));

    private static readonly IBrush BackgroundBrush = SolidColorBrush.Parse("#10141b");
    private static readonly IBrush NodeFillBrush = SolidColorBrush.Parse("#182130");
    private static readonly IBrush SelectedFillBrush = SolidColorBrush.Parse("#25344a");
    private static readonly IBrush NodeStrokeBrush = SolidColorBrush.Parse("#344157");
    private static readonly IBrush SelectedStrokeBrush = SolidColorBrush.Parse("#ffd166");
    private static readonly IBrush BadgeFillBrush = SolidColorBrush.Parse("#121924");
    private static readonly IBrush BadgeStrokeBrush = SolidColorBrush.Parse("#57c7ff");
    private static readonly IBrush TextBrush = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush MutedBrush = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush EdgeBrush = SolidColorBrush.Parse("#4f6487");
    private static readonly Typeface MonoTypeface = new("monospace");
    private readonly List<NodeLayout> _layouts = [];

    public HierarchyNodeViewModel? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public string? SelectedPath
    {
        get => GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    public IEnumerable<HierarchyTraceScopeSummaryViewModel>? ScopeSummaries
    {
        get => GetValue(ScopeSummariesProperty);
        set => SetValue(ScopeSummariesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(BackgroundBrush, bounds);

        if (Root is null)
        {
            DrawText(context, "Hierarchy graph will appear after loading a project.", 14, 30, MutedBrush, 13);
            return;
        }

        _layouts.Clear();
        Dictionary<string, HierarchyTraceScopeSummaryViewModel> summaries = (ScopeSummaries ?? Array.Empty<HierarchyTraceScopeSummaryViewModel>())
            .ToDictionary(summary => summary.HierarchyPath, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, List<HierarchyNodeViewModel>> byDepth = [];
        CollectByDepth(Root, 0, byDepth);

        double columnWidth = Math.Max(220, (bounds.Width - 40) / Math.Max(1, byDepth.Keys.DefaultIfEmpty(0).Max() + 1));
        foreach ((int depth, List<HierarchyNodeViewModel> nodes) in byDepth.OrderBy(static pair => pair.Key))
        {
            double nodeHeight = 56;
            double verticalGap = 18;
            double totalHeight = nodes.Count * nodeHeight + Math.Max(0, nodes.Count - 1) * verticalGap;
            double startY = Math.Max(20, (bounds.Height - totalHeight) / 2);
            for (int index = 0; index < nodes.Count; index++)
            {
                Rect rect = new(
                    20 + depth * columnWidth,
                    startY + index * (nodeHeight + verticalGap),
                    Math.Min(180, columnWidth - 40),
                    nodeHeight);
                _layouts.Add(new NodeLayout(nodes[index], rect));
            }
        }

        foreach (NodeLayout layout in _layouts)
        {
            foreach (HierarchyNodeViewModel child in layout.Node.Children)
            {
                NodeLayout? childLayout = _layouts.FirstOrDefault(candidate => ReferenceEquals(candidate.Node, child));
                if (childLayout is null)
                {
                    continue;
                }

                context.DrawLine(
                    new Pen(EdgeBrush, 1.5),
                    new Point(layout.Bounds.Right, layout.Bounds.Center.Y),
                    new Point(childLayout.Bounds.X, childLayout.Bounds.Center.Y));
            }
        }

        foreach (NodeLayout layout in _layouts)
        {
            bool isSelected = string.Equals(layout.Node.HierarchyPath, SelectedPath, StringComparison.Ordinal);
            context.FillRectangle(isSelected ? SelectedFillBrush : NodeFillBrush, layout.Bounds, 8);
            context.DrawRectangle(new Pen(isSelected ? SelectedStrokeBrush : NodeStrokeBrush, isSelected ? 2 : 1.2), layout.Bounds, 8);
            DrawText(context, layout.Node.InstanceName, layout.Bounds.X + 12, layout.Bounds.Y + 10, TextBrush, 12);
            DrawText(context, layout.Node.ModuleName, layout.Bounds.X + 12, layout.Bounds.Y + 29, MutedBrush, 11);
            if (summaries.TryGetValue(layout.Node.HierarchyPath, out HierarchyTraceScopeSummaryViewModel? summary)
                && (summary.ExactSignalCount > 0 || summary.DescendantSignalCount > 0))
            {
                DrawScopeBadges(context, layout.Bounds, summary);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point point = e.GetPosition(this);
        NodeLayout? hit = _layouts.FirstOrDefault(layout => layout.Bounds.Contains(point));
        if (hit is not null)
        {
            SelectedPath = hit.Node.HierarchyPath;
            e.Handled = true;
        }
    }

    private static void CollectByDepth(HierarchyNodeViewModel node, int depth, IDictionary<int, List<HierarchyNodeViewModel>> byDepth)
    {
        if (!byDepth.TryGetValue(depth, out List<HierarchyNodeViewModel>? nodes))
        {
            nodes = [];
            byDepth[depth] = nodes;
        }

        nodes.Add(node);
        foreach (HierarchyNodeViewModel child in node.Children)
        {
            CollectByDepth(child, depth + 1, byDepth);
        }
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, IBrush brush, double size)
    {
        FormattedText formatted = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private static void DrawScopeBadges(DrawingContext context, Rect bounds, HierarchyTraceScopeSummaryViewModel summary)
    {
        string exactText = $"S {summary.ExactSignalCount}";
        string nestedText = $"D {summary.DescendantSignalCount}";

        double exactWidth = Math.Max(34, MeasureWidth(exactText, 10) + 12);
        double nestedWidth = Math.Max(34, MeasureWidth(nestedText, 10) + 12);
        Rect exactRect = new(bounds.Right - exactWidth - nestedWidth - 16, bounds.Bottom - 22, exactWidth, 16);
        Rect nestedRect = new(bounds.Right - nestedWidth - 8, bounds.Bottom - 22, nestedWidth, 16);

        DrawBadge(context, exactRect, exactText, summary.ExactSignalCount > 0 ? BadgeStrokeBrush : MutedBrush);
        DrawBadge(context, nestedRect, nestedText, summary.DescendantSignalCount > 0 ? SelectedStrokeBrush : MutedBrush);
    }

    private static void DrawBadge(DrawingContext context, Rect rect, string text, IBrush strokeBrush)
    {
        context.FillRectangle(BadgeFillBrush, rect, 4);
        context.DrawRectangle(new Pen(strokeBrush, 1), rect, 4);
        DrawText(context, text, rect.X + 6, rect.Y + 2, strokeBrush, 10);
    }

    private static double MeasureWidth(string text, double size)
    {
        FormattedText formatted = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            TextBrush);
        return formatted.Width;
    }

    private sealed record NodeLayout(HierarchyNodeViewModel Node, Rect Bounds);
}
