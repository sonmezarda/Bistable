using Avalonia;
using Avalonia.Media;
using Bistable.Core.Design;
using Bistable.App.Services;
using Bistable.App.Services.Layout;
using Bistable.App.ViewModels;

namespace Bistable.App.Views;

public sealed partial class SchematicPreviewControl
{
    private void DrawCollapsedTopSymbol(
        DrawingContext context,
        Rect worldBounds,
        Rect moduleRect,
        IReadOnlyList<SignalViewModel> inputs,
        IReadOnlyList<SignalViewModel> outputs,
        double laneHeight,
        bool canExpand)
    {
        context.FillRectangle(Palette.ModuleFill, moduleRect, 10);
        context.DrawRectangle(new Pen(Palette.ModuleStroke, 1.5), moduleRect, 10);
        DrawText(context, ModuleName, moduleRect.X + 18, moduleRect.Y + 18, Palette.Text, 20);
        DrawText(context, "Top-level symbol", moduleRect.X + 18, moduleRect.Y + 46, Palette.Muted, 12);
        DrawText(context, "Click pins to drive", moduleRect.X + 18, moduleRect.Bottom - 28, Palette.Muted, 11);
        if (canExpand)
        {
            DrawScopeExpansionButton(context, moduleRect, ActiveScopePath, expanded: false);
        }

        DrawPins(context, inputs, worldBounds, moduleRect, leftSide: true, laneHeight);
        DrawPins(context, outputs, worldBounds, moduleRect, leftSide: false, laneHeight);
    }

    private void DrawCollapsedScopeSymbol(
        DrawingContext context,
        Rect moduleRect,
        IReadOnlyList<HierarchyScopePortViewModel> ports,
        bool canExpand)
    {
        context.FillRectangle(Palette.ModuleFill, moduleRect, 10);
        context.DrawRectangle(new Pen(Palette.ModuleStroke, 1.5), moduleRect, 10);
        DrawText(context, Ellipsize(ActiveScopeTitle ?? "Scope", 20, moduleRect.Width - 72), moduleRect.X + 18, moduleRect.Y + 18, Palette.Text, 20);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 12, moduleRect.Width - 72), moduleRect.X + 18, moduleRect.Y + 48, Palette.PinStroke, 12);
        DrawText(context, "Click + to inspect internals", moduleRect.X + 18, moduleRect.Bottom - 28, Palette.Muted, 11);
        if (canExpand)
        {
            DrawScopeExpansionButton(context, moduleRect, ActiveScopePath, expanded: false);
        }

        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(CompactLayout ? 6 : 9).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(CompactLayout ? 6 : 9).ToList();
        double laneCount = Math.Max(inputs.Count, outputs.Count);
        double laneHeight = Math.Max(24, Math.Min(42, (moduleRect.Height - 112) / Math.Max(1, laneCount)));

        for (int index = 0; index < inputs.Count; index++)
        {
            DrawCollapsedScopePort(context, inputs[index], moduleRect, index, laneHeight, leftSide: true);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            DrawCollapsedScopePort(context, outputs[index], moduleRect, index, laneHeight, leftSide: false);
        }
    }

    private void DrawCollapsedScopePort(
        DrawingContext context,
        HierarchyScopePortViewModel port,
        Rect moduleRect,
        int index,
        double laneHeight,
        bool leftSide)
    {
        double y = moduleRect.Y + 86 + index * laneHeight;
        double badgeWidth = CompactLayout ? 48 : 58;
        IBrush stroke = leftSide ? Palette.PinStroke : Palette.OutputValue;
        string label = Ellipsize(port.Name, 10, CompactLayout ? 82 : 118);

        if (leftSide)
        {
            double pinStartX = moduleRect.X - 44;
            double pinEndX = moduleRect.X;
            Rect badge = new(Math.Max(16, pinStartX - badgeWidth - 6), y - 10, badgeWidth, 20);
            double labelRight = badge.X - 10;
            double labelWidth = MeasureWidth(label, 10);
            double inputLabelX = Math.Max(16, labelRight - labelWidth);

            context.DrawLine(new Pen(stroke, 1.4), new Point(pinStartX, y), new Point(pinEndX, y));
            context.FillRectangle(stroke, new Rect(moduleRect.X - 2, y - 2, 4, 4));
            DrawText(context, label, inputLabelX, y - 7, Palette.Muted, 10);
            DrawMiniBadge(context, badge, port.WidthLabel, stroke);
            return;
        }

        double outputStartX = moduleRect.Right;
        double outputEndX = moduleRect.Right + 44;
        double labelX = outputEndX + 10;
        Rect outputBadge = new(labelX + MeasureWidth(label, 10) + 10, y - 10, badgeWidth, 20);
        context.DrawLine(new Pen(stroke, 1.4), new Point(outputStartX, y), new Point(outputEndX, y));
        context.FillRectangle(stroke, new Rect(moduleRect.Right - 2, y - 2, 4, 4));
        DrawText(context, label, labelX, y - 7, Palette.Muted, 10);
        DrawMiniBadge(context, outputBadge, port.WidthLabel, stroke);
    }

    private void DrawPins(
        DrawingContext context,
        IReadOnlyList<SignalViewModel> signals,
        Rect bounds,
        Rect moduleRect,
        bool leftSide,
        double laneHeight)
    {
        for (int index = 0; index < signals.Count; index++)
        {
            SignalViewModel signal = signals[index];
            double y = moduleRect.Y + 86 + index * laneHeight;
            bool isSelected = string.Equals(signal.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);
            IBrush stroke = isSelected ? Palette.Selected : Palette.PinStroke;
            IBrush text = isSelected ? Palette.Selected : Palette.Text;
            double badgeWidth = GetValueBadgeWidth(signal.Value, 62, 98);

            string truncatedLabel = signal.Name;
            BoundaryPinLayout layout = ComputeBoundaryPinLayout(
                bounds, moduleRect, y, badgeWidth, leftSide,
                rawLabel: signal.Name,
                measureLabel: (lbl, max) =>
                {
                    truncatedLabel = Ellipsize(lbl, 12, max);
                    return MeasureWidth(truncatedLabel, 12);
                });

            _signalHitTargets.Add(new SignalHitTarget(signal, layout.Hit));
            context.DrawLine(new Pen(stroke, 2), layout.WireStart, layout.WireEnd);

            if (leftSide)
            {
                DrawText(context, truncatedLabel, layout.LabelX, y - 8, text, 12);
                DrawValueBadge(
                    context,
                    signal.Value,
                    layout.Badge,
                    signal.IsBoolean && signal.Value == "1" ? Palette.Selected : Palette.InputValue,
                    Palette.ValueFill);
            }
            else
            {
                DrawValueBadge(context, signal.Value, layout.Badge, isSelected ? Palette.Selected : Palette.OutputValue, Palette.ValueFill);
                DrawText(context, truncatedLabel, layout.LabelX, y - 8, text, 12);
            }

            context.FillRectangle(stroke, new Rect(leftSide ? moduleRect.X - 3 : moduleRect.Right - 3, y - 3, 6, 6));
        }
    }

    /// <summary>
    /// Pure geometry for top-level boundary pin layout. Outputs the badge rect,
    /// label x-position, wire endpoints, and hit-test rect for one pin. Mirroring
    /// is exact: input and output layouts produce identical badge-to-wire gaps
    /// (P2.5-1 fix). Extracted as a public static for testability.
    /// </summary>
    public static BoundaryPinLayout ComputeBoundaryPinLayout(
        Rect bounds,
        Rect moduleRect,
        double y,
        double badgeWidth,
        bool leftSide,
        string rawLabel,
        Func<string, double, double> measureLabel)
    {
        const double WireLength = 44;
        const double BadgeGap = 6;       // badge ↔ wire end (or wire start) gap
        const double LabelGap = 10;      // label ↔ badge gap

        if (leftSide)
        {
            double pinStartX = moduleRect.X - WireLength;
            double pinEndX = moduleRect.X;
            double badgeX = Math.Max(16, pinStartX - badgeWidth - BadgeGap);
            Rect badge = new(badgeX, y - 12, badgeWidth, 24);
            double labelRightLimit = badge.X - LabelGap;
            double labelMaxWidth = Math.Max(62, labelRightLimit - 16);
            double labelWidth = measureLabel(rawLabel, labelMaxWidth);
            double labelX = Math.Max(16, labelRightLimit - labelWidth);
            Rect hit = new(labelX - 8, y - 16, moduleRect.X - labelX + 8, 32);
            return new BoundaryPinLayout(
                Badge: badge,
                LabelX: labelX,
                WireStart: new Point(pinStartX, y),
                WireEnd: new Point(pinEndX, y),
                Hit: hit);
        }
        else
        {
            double pinStartX = moduleRect.Right;
            double pinEndX = moduleRect.Right + WireLength;
            double badgeX = pinEndX + BadgeGap;
            Rect badge = new(badgeX, y - 12, badgeWidth, 24);
            double labelX = badge.Right + LabelGap;
            double labelMaxWidth = Math.Max(60, bounds.Right - labelX - 16);
            double labelWidth = measureLabel(rawLabel, labelMaxWidth);
            Rect hit = new(moduleRect.Right - 8, y - 16,
                Math.Max(badge.Right, labelX + labelWidth) - moduleRect.Right + 16, 32);
            return new BoundaryPinLayout(
                Badge: badge,
                LabelX: labelX,
                WireStart: new Point(pinStartX, y),
                WireEnd: new Point(pinEndX, y),
                Hit: hit);
        }
    }

    public readonly record struct BoundaryPinLayout(
        Rect Badge,
        double LabelX,
        Point WireStart,
        Point WireEnd,
        Rect Hit);

    private Rect? DrawScopeCard(DrawingContext context, Rect bounds)
    {
        if (!HasScopeCardContent())
        {
            return null;
        }

        string title = string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!;
        string moduleName = string.IsNullOrWhiteSpace(ActiveScopeModuleName) ? "module" : ActiveScopeModuleName!;
        string path = string.IsNullOrWhiteSpace(ActiveScopePath) ? string.Empty : ActiveScopePath!;
        string summary = string.IsNullOrWhiteSpace(ActiveScopeSummary) ? string.Empty : ActiveScopeSummary!;

        double contentWidth = new[]
        {
            MeasureWidth(title, 12),
            MeasureWidth(moduleName, 11),
            path.Length == 0 ? 0 : MeasureWidth(path, 10),
            summary.Length == 0 ? 0 : MeasureWidth(summary, 10)
        }.Max();
        double cardWidth = Math.Clamp(contentWidth + 28, 210, 320);
        double cardHeight = 72 + (summary.Length == 0 ? 0 : 16);
        Rect card = new(bounds.Right - cardWidth - 16, 16, cardWidth, cardHeight);

        context.FillRectangle(Palette.ValueFill, card, 6);
        context.DrawRectangle(new Pen(Palette.PinStroke, 1), card, 6);
        DrawText(context, title, card.X + 12, card.Y + 8, Palette.Text, 12);
        DrawText(context, Ellipsize(moduleName, 11, card.Width - 24), card.X + 12, card.Y + 28, Palette.PinStroke, 11);
        if (path.Length > 0)
        {
            DrawText(context, Ellipsize(path, 10, card.Width - 24), card.X + 12, card.Y + 46, Palette.Muted, 10);
        }

        if (summary.Length > 0)
        {
            DrawText(context, Ellipsize(summary, 10, card.Width - 24), card.X + 12, card.Bottom - 16, Palette.Muted, 10);
        }

        return card;
    }

    private void DrawExpandedScopePanel(
        DrawingContext context,
        Rect bounds,
        Rect moduleRect,
        Rect? scopeCard,
        IReadOnlyList<SignalViewModel> scopeSignals,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        IReadOnlyList<DesignContAssign> contAssigns,
        IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive> scopePrimitives,
        IReadOnlyDictionary<string, IReadOnlyList<Bistable.Core.Design.Schematic.SchematicPrimitive>>? scopePrimitivesByModule)
    {
        if (RoutingEngine == SchematicRoutingEngine.Elk)
        {
            DrawElkScopePanel(context, bounds, moduleRect, scopeSignals, childScopes, scopePorts, localSignals, contAssigns, scopePrimitives, scopePrimitivesByModule);
            return;
        }

        if (RoutingEngine == SchematicRoutingEngine.GraphvizDot)
        {
            DrawGraphvizDotScopePanel(context, bounds, moduleRect, scopeSignals, childScopes, scopePorts, localSignals);
            return;
        }

        int maxChildConnectionRows = childScopes
            .Select(scope => Math.Max(
                scope.PortConnections.Count(static connection => connection.IsInput),
                scope.PortConnections.Count(static connection => connection.IsOutput)))
            .DefaultIfEmpty(0)
            .Max();
        SchematicScopePanelLayout layout = ScopeLayoutEngine.Compute(new SchematicScopeLayoutInput(
            bounds,
            moduleRect,
            scopeCard?.Bottom,
            CompactLayout,
            ParentVisible: false,
            scopeSignals.Count,
            childScopes.Count,
            localSignals.Count,
            scopePorts.Count(static port => port.IsInput),
            scopePorts.Count(static port => port.IsOutput),
            maxChildConnectionRows));
        Rect panel = layout.PanelRect;
        int visibleProbeCount = Math.Min(scopeSignals.Count, CompactLayout ? 10 : 18);
        int visibleChildCount = Math.Min(childScopes.Count, CompactLayout ? 6 : 10);
        int visibleLocalCount = Math.Min(localSignals.Count, CompactLayout ? 8 : 14);
        IReadOnlyList<Rect> effectiveChildRects = BuildEffectiveChildRects(layout, childScopes, visibleChildCount);
        layout = BuildEffectiveScopeLayout(layout, effectiveChildRects, visibleLocalCount, bounds);
        panel = layout.PanelRect;
        _lastFocusedScopePanelRect = panel;

        context.FillRectangle(Palette.FocusPanelFill, panel, 8);
        context.DrawRectangle(new Pen(Palette.ModuleStroke, 1.2), panel, 8);
        DrawScopeExpansionButton(context, panel, ActiveScopePath, expanded: true);

        DrawText(context, string.IsNullOrWhiteSpace(ActiveScopeTitle) ? "Scope" : ActiveScopeTitle!, panel.X + 16, panel.Y + 12, Palette.Text, 13);
        DrawText(context, Ellipsize(ActiveScopeModuleName ?? "module", 11, panel.Width - 32), panel.X + 16, panel.Y + 34, Palette.PinStroke, 11);
        if (!string.IsNullOrWhiteSpace(ActiveScopePath))
        {
            DrawText(context, Ellipsize(ActiveScopePath!, 10, panel.Width - 32), panel.X + 16, panel.Y + 52, Palette.Muted, 10);
        }

        CurrentPortLayout currentPortLayout = DrawScopeBoundaryPorts(
            context,
            layout,
            scopePorts);
        IReadOnlyList<ChildNodeLayout> childLayouts = DrawNavigationNeighborhood(
            context,
            layout,
            childScopes,
            visibleChildCount);

        double localsTop = layout.LocalSectionRect?.Y ?? (GetLayoutContentBottom(layout) + 24);
        IReadOnlyDictionary<string, LocalSignalAnchor> localSignalAnchors = DrawLocalSignalSection(context, layout, localSignals, visibleLocalCount, localsTop);
        DrawConnectionRoutes(context, currentPortLayout, childLayouts, localSignalAnchors);
        DrawScopeBoundaryPorts(context, layout, scopePorts);
        for (int index = 0; index < childLayouts.Count; index++)
        {
            DrawScopeNodeCard(context, childLayouts[index].Instance, childLayouts[index].Bounds, role: "child");
        }

        DrawLocalSignalSection(context, layout, localSignals, visibleLocalCount, localsTop);

        DrawScopeProbeSection(context, layout, scopeSignals, visibleProbeCount);

        string footer = BuildScopeFooter(scopeSignals.Count, visibleProbeCount, childScopes.Count);
        DrawText(context, Ellipsize(footer, 10, panel.Width - 32), panel.X + 16, panel.Bottom - 18, Palette.Muted, 10);
    }

    private CurrentPortLayout DrawScopeBoundaryPorts(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts)
    {
        Rect boundary = BuildScopeBoundaryRect(layout.PanelRect);
        IReadOnlyDictionary<string, PortAnchor> anchors = DrawScopeBoundaryPortGlyphs(context, layout.PanelRect, boundary, scopePorts, attachToEdges: true);
        return new CurrentPortLayout(layout, boundary, anchors);
    }

    private IReadOnlyList<ChildNodeLayout> DrawNavigationNeighborhood(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount)
    {
        List<ChildNodeLayout> layouts = [];
        if (visibleChildCount == 0)
        {
            return layouts;
        }

        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect childRect = layout.ChildNodeRects[index];
            layouts.Add(DrawScopeNodeCard(context, childScopes[index], childRect, role: "child"));
        }

        return layouts;
    }

    private IReadOnlyList<Rect> BuildEffectiveChildRects(
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childScopes,
        int visibleChildCount)
    {
        if (visibleChildCount == 0)
        {
            return [];
        }

        List<Rect> rects = [];
        double nextY = double.NegativeInfinity;
        double gap = CompactLayout ? 24 : 32;
        double? inlineShift = null;
        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect rect = layout.ChildNodeRects[index];
            if (layout.InlineChildren)
            {
                inlineShift ??= Math.Max(
                    0,
                    rect.X - (layout.PanelRect.X + (CompactLayout ? 300 : 360)));
                rect = new Rect(rect.X - inlineShift.Value, rect.Y, rect.Width, rect.Height);
            }

            if (rect.Y < nextY)
            {
                rect = new Rect(rect.X, nextY, rect.Width, rect.Height);
            }

            if (IsScopeExpanded(childScopes[index].HierarchyPath) && childScopes[index].ChildInstances.Count > 0)
            {
                rect = ExpandChildRectForNestedScope(rect, childScopes[index]);
            }

            rects.Add(rect);
            nextY = rect.Bottom + gap;
        }

        return rects;
    }

    private Rect ExpandChildRectForNestedScope(Rect rect, HierarchyScopeInstanceViewModel scope)
    {
        int visibleChildCount = Math.Min(scope.ChildInstances.Count, CompactLayout ? 5 : 8);
        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 8 : 14);
        int maxPortRows = Math.Max(
            Math.Max(scope.InputCount, scope.OutputCount),
            scope.ChildInstances
                .Select(child => Math.Max(
                    child.PortConnections.Count(static connection => connection.IsInput),
                    child.PortConnections.Count(static connection => connection.IsOutput)))
                .DefaultIfEmpty(0)
                .Max());
        double width = Math.Max(rect.Width, CompactLayout ? 760 : 980);
        double childHeight = CompactLayout ? 104 : 132;
        double height = Math.Max(
            rect.Height,
            (CompactLayout ? 168 : 204)
            + Math.Max(0, visibleChildCount) * (childHeight + (CompactLayout ? 18 : 24))
            + Math.Max(0, visibleLocalCount) * (CompactLayout ? 22 : 28)
            + Math.Max(0, maxPortRows - 3) * (CompactLayout ? 12 : 16));

        return new Rect(rect.X, rect.Y, width, Math.Clamp(height, CompactLayout ? 320 : 400, CompactLayout ? 780 : 980));
    }

    private SchematicScopePanelLayout BuildEffectiveScopeLayout(
        SchematicScopePanelLayout layout,
        IReadOnlyList<Rect> childRects,
        int visibleLocalCount,
        Rect worldBounds)
    {
        Rect panel = layout.PanelRect;
        double requiredRight = childRects.Count == 0
            ? panel.Right
            : Math.Max(panel.Right, childRects.Max(static rect => rect.Right) + (CompactLayout ? 18 : 24));
        if (requiredRight > panel.Right)
        {
            double nextWidth = Math.Min(requiredRight - panel.X, Math.Max(panel.Width, worldBounds.Right - panel.X - 16));
            panel = new Rect(panel.X, panel.Y, nextWidth, panel.Height);
        }

        double contentBottom = childRects.Count == 0
            ? layout.CurrentNodeRect.Bottom
            : Math.Max(layout.CurrentNodeRect.Bottom, childRects.Max(static rect => rect.Bottom));
        double margin = CompactLayout ? 18 : 22;
        double localsTop = contentBottom + (CompactLayout ? 38 : 46);
        double localHeight = layout.LocalSectionRect?.Height ?? 0;
        Rect? localSection = visibleLocalCount == 0
            ? null
            : new Rect(panel.X + margin, localsTop, panel.Width - margin * 2, localHeight);
        double probeTop = localsTop + localHeight + (localHeight > 0 ? (CompactLayout ? 16 : 20) : 0);
        Rect probeSection = new Rect(panel.X + margin, probeTop, panel.Width - margin * 2, layout.ProbeSectionRect.Height);
        double requiredHeight = probeSection.Bottom + (CompactLayout ? 36 : 44) - panel.Y;
        double maxHeight = Math.Max(panel.Height, panel.Bottom + (CompactLayout ? 340 : 460) - panel.Y);
        panel = new Rect(panel.X, panel.Y, panel.Width, Math.Min(Math.Max(panel.Height, requiredHeight), maxHeight));

        return layout with
        {
            PanelRect = panel,
            ChildNodeRects = childRects,
            LocalSectionRect = localSection,
            ProbeSectionRect = probeSection
        };
    }

    private static double GetLayoutContentBottom(SchematicScopePanelLayout layout) =>
        layout.ChildNodeRects.Count == 0
            ? layout.CurrentNodeRect.Bottom
            : Math.Max(layout.CurrentNodeRect.Bottom, layout.ChildNodeRects.Max(static rect => rect.Bottom));

    private static IReadOnlyList<HierarchyScopeInstanceViewModel> OrderChildScopesForLayout(
        IReadOnlyList<HierarchyScopeInstanceViewModel> children,
        IReadOnlyList<HierarchyScopePortViewModel> scopePorts) =>
        HierarchicalLayoutEngine.OrderForLayout(children, scopePorts);

    private static Rect BuildScopeBoundaryRect(Rect panel)
    {
        double topInset = 80;
        double bottomInset = 80;
        return new Rect(
            panel.X,
            panel.Y + topInset,
            panel.Width,
            Math.Max(80, panel.Height - topInset - bottomInset));
    }

    private SchematicScopePanelLayout BuildNestedScopeLayout(
        Rect rect,
        HierarchyScopeInstanceViewModel scope,
        IReadOnlyList<HierarchyScopeInstanceViewModel> orderedChildren)
    {
        Rect panel = new(rect.X, rect.Y + 64, rect.Width, Math.Max(120, rect.Height - 92));
        int visibleChildCount = Math.Min(orderedChildren.Count, CompactLayout ? 5 : 8);
        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 8 : 14);
        int visibleInputCount = Math.Min(scope.Ports.Count(static port => port.IsInput), CompactLayout ? 5 : 8);
        int visibleOutputCount = Math.Min(scope.Ports.Count(static port => port.IsOutput), CompactLayout ? 5 : 8);
        double boundaryHeight = Math.Clamp(
            (CompactLayout ? 88 : 112) + Math.Max(visibleInputCount, visibleOutputCount) * (CompactLayout ? 18 : 22),
            CompactLayout ? 138 : 170,
            Math.Max(CompactLayout ? 160 : 210, panel.Height - 64));
        Rect boundaryRect = new(rect.X, panel.Y + 28, rect.Width, boundaryHeight);
        double childX = panel.X + Math.Clamp(panel.Width * 0.30, CompactLayout ? 230 : 300, CompactLayout ? 360 : 480);
        double rightBoundaryReserve = CompactLayout ? 190 : 240;
        double childWidth = Math.Max(CompactLayout ? 240 : 300, panel.Right - childX - rightBoundaryReserve);
        double childHeight = CompactLayout ? 104 : 132;
        double childGap = CompactLayout ? 18 : 24;
        List<Rect> childRects = [];
        for (int index = 0; index < visibleChildCount; index++)
        {
            Rect childRect = new(childX, boundaryRect.Y + index * (childHeight + childGap), childWidth, childHeight);
            if (IsScopeExpanded(orderedChildren[index].HierarchyPath) && orderedChildren[index].ChildInstances.Count > 0)
            {
                childRect = ExpandChildRectForNestedScope(childRect, orderedChildren[index]);
            }

            childRects.Add(childRect);
        }

        double contentBottom = childRects.Count == 0
            ? boundaryRect.Bottom
            : Math.Max(boundaryRect.Bottom, childRects.Max(static child => child.Bottom));
        double localHeight = visibleLocalCount == 0
            ? 0
            : (CompactLayout ? 42 : 54) + Math.Max(0, visibleLocalCount - 1) * (CompactLayout ? 8 : 10);
        Rect? localSection = visibleLocalCount == 0
            ? null
            : new Rect(panel.X + 14, contentBottom + (CompactLayout ? 22 : 28), panel.Width - 28, localHeight);
        Rect probeSection = new(panel.X + 14, (localSection?.Bottom ?? contentBottom) + 12, panel.Width - 28, 20);

        return new SchematicScopePanelLayout(
            panel,
            boundaryRect,
            null,
            childRects,
            localSection,
            probeSection,
            InlineChildren: true,
            RouteCorridorWidth: Math.Max(80, childX - boundaryRect.Right - 24),
            ChildCardWidth: childWidth,
            ChildCardHeight: childHeight,
            TitleBlockHeight: 0,
            LocalColumns: Math.Max(1, Math.Min(3, visibleLocalCount)),
            LocalRowCount: visibleLocalCount,
            ProbeColumns: 1,
            ProbeRowCount: 0);
    }

    private void DrawScopeProbeSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<SignalViewModel> scopeSignals,
        int visibleProbeCount)
    {
        Rect panel = layout.PanelRect;
        double top = layout.ProbeSectionRect.Y;
        DrawText(context, "Exact-scope probes", panel.X + 16, top, Palette.Text, 11);
        DrawText(context, "local traced signals", panel.Right - (CompactLayout ? 116 : 134), top, Palette.Muted, 10);

        if (visibleProbeCount == 0)
        {
            DrawText(context, "No exact-scope probes are available.", panel.X + 16, top + 22, Palette.Muted, 10);
            if (!string.IsNullOrWhiteSpace(ActiveScopeHint))
            {
                DrawText(context, Ellipsize(ActiveScopeHint!, 10, panel.Width - 32), panel.X + 16, top + 40, Palette.Muted, 10);
            }

            return;
        }

        int columns = layout.ProbeColumns;
        double columnGap = CompactLayout ? 14 : 18;
        double itemWidth = columns == 1
            ? panel.Width - 32
            : (panel.Width - 32 - columnGap) / 2;
        if (!CompactLayout && columns == 3)
        {
            itemWidth = (panel.Width - 32 - columnGap * 2) / 3;
        }

        for (int index = 0; index < visibleProbeCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double itemX = panel.X + 16 + column * (itemWidth + columnGap);
            double itemY = top + 20 + row * (CompactLayout ? 30 : 34);
            DrawScopeProbe(context, scopeSignals[index], new Rect(itemX, itemY, itemWidth, 24));
        }
    }

    private void DrawScopeProbe(DrawingContext context, SignalViewModel signal, Rect rect)
    {
        bool isSelected = string.Equals(signal.Name, SelectedSignalName, StringComparison.OrdinalIgnoreCase);
        IBrush stroke = isSelected ? Palette.Selected : Palette.PinStroke;
        IBrush labelBrush = isSelected ? Palette.Selected : Palette.Text;
        double badgeWidth = GetValueBadgeWidth(signal.Value, 54, 92);
        Rect badge = new(rect.Right - badgeWidth, rect.Y + 2, badgeWidth, 20);
        double centerY = rect.Y + rect.Height / 2;
        Rect lineRect = new(rect.X + 4, centerY - 3, 6, 6);

        if (isSelected)
        {
            context.FillRectangle(Palette.ScopeHighlight, rect, 5);
        }

        context.DrawRectangle(new Pen(isSelected ? Palette.Selected : Palette.ModuleStroke, isSelected ? 1.2 : 1), rect, 5);
        context.FillRectangle(stroke, lineRect);

        double labelStart = rect.X + 16;
        double labelEndReserve = badge.X - 12;
        if (signal.IsInWaveform)
        {
            Rect waveformBadge = new(badge.X - 28, rect.Y + 2, 20, 20);
            DrawMiniBadge(context, waveformBadge, "W", Palette.OutputValue);
            labelEndReserve = waveformBadge.X - 8;
        }

        string label = Ellipsize(signal.ShortName, 11, Math.Max(48, labelEndReserve - labelStart));
        DrawText(context, label, labelStart, rect.Y + 4, labelBrush, 11);
        DrawValueBadge(context, signal.Value, badge, stroke, Palette.ValueFill);

        _signalHitTargets.Add(new SignalHitTarget(signal, rect));
    }

    private ChildNodeLayout DrawScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        return IsScopeExpanded(scope.HierarchyPath) && scope.ChildInstances.Count > 0
            ? DrawExpandedScopeNodeCard(context, scope, rect, role)
            : DrawCollapsedScopeNodeCard(context, scope, rect, role);
    }

    private ChildNodeLayout DrawCollapsedScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush fill = selected ? Palette.NodeSelectedFill : Palette.NodeFill;
        IBrush stroke = selected ? Palette.Selected : (scope.HasTraceActivity ? Palette.PinStroke : Palette.ModuleStroke);
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputConnections = scope.PortConnections.Where(static connection => connection.IsInput).Take(CompactLayout ? 6 : 8).ToList();
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputConnections = scope.PortConnections.Where(static connection => connection.IsOutput).Take(CompactLayout ? 5 : 8).ToList();
        SchematicNodeCardLayout layout = NodeCardLayoutEngine.Compute(new SchematicNodeCardLayoutInput(
            rect,
            CompactLayout,
            inputConnections.Count,
            outputConnections.Count,
            scope.InputCount,
            scope.OutputCount));

        context.FillRectangle(fill, rect, 7);
        context.DrawRectangle(new Pen(stroke, selected ? 1.4 : 1), rect, 7);
        context.DrawLine(new Pen(Palette.ModuleStroke, 1), new Point(layout.HeaderRect.X + 8, layout.HeaderRect.Bottom), new Point(layout.HeaderRect.Right - 8, layout.HeaderRect.Bottom));
        context.DrawLine(new Pen(Palette.ModuleStroke, 1), new Point(layout.FooterRect.X + 8, layout.FooterRect.Y), new Point(layout.FooterRect.Right - 8, layout.FooterRect.Y));
        DrawText(context, Ellipsize(scope.InstanceName, 11, rect.Width - 84), rect.X + 10, rect.Y + 8, Palette.Text, 11);
        DrawText(context, Ellipsize(scope.ModuleName, 10, rect.Width - 84), rect.X + 10, rect.Y + 24, Palette.Muted, 10);
        DrawMiniBadge(context, new Rect(rect.Right - 34, rect.Y + 7, 24, 14), role[..1].ToUpperInvariant(), Palette.Muted);
        if (scope.ChildInstances.Count > 0)
        {
            DrawScopeExpansionButton(context, rect, scope.HierarchyPath, expanded: false);
        }

        DrawMiniBadge(context, new Rect(rect.Right - 72, rect.Bottom - 20, 64, 16), scope.ScopeBadgeText, stroke);
        if (inputConnections.Count == 0 && outputConnections.Count == 0)
        {
            DrawPortCountStubs(context, rect, scope.InputCount, scope.OutputCount, stroke);
        }

        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect, CanExpand: scope.ChildInstances.Count > 0));
        IReadOnlyDictionary<string, Point> anchors = DrawChildConnectionStubs(context, scope, layout, inputConnections, outputConnections);
        return new ChildNodeLayout(scope, rect, anchors);
    }

    private ChildNodeLayout DrawExpandedScopeNodeCard(DrawingContext context, HierarchyScopeInstanceViewModel scope, Rect rect, string role)
    {
        bool selected = string.Equals(scope.HierarchyPath, ActiveScopePath, StringComparison.OrdinalIgnoreCase);
        IBrush stroke = selected ? Palette.Selected : Palette.PinStroke;
        context.FillRectangle(Palette.NodeFill, rect, 8);
        context.DrawRectangle(new Pen(stroke, selected ? 1.5 : 1.1), rect, 8);

        Rect header = new(rect.X + 12, rect.Y + 10, rect.Width - 24, 44);
        DrawText(context, Ellipsize(scope.InstanceName, 12, header.Width - 92), header.X, header.Y + 1, Palette.Text, 12);
        DrawText(context, Ellipsize(scope.ModuleName, 10, header.Width - 92), header.X, header.Y + 22, Palette.PinStroke, 10);
        DrawMiniBadge(context, new Rect(rect.Right - 72, rect.Y + 13, 34, 16), role[..1].ToUpperInvariant(), Palette.Muted);
        DrawScopeExpansionButton(context, rect, scope.HierarchyPath, expanded: true);
        context.DrawLine(new Pen(Palette.ModuleStroke, 1), new Point(rect.X + 12, rect.Y + 58), new Point(rect.Right - 12, rect.Y + 58));

        IReadOnlyList<HierarchyScopeInstanceViewModel> orderedChildren = OrderChildScopesForLayout(scope.ChildInstances, scope.Ports);
        SchematicScopePanelLayout nestedLayout = BuildNestedScopeLayout(rect, scope, orderedChildren);
        CurrentPortLayout boundaryLayout = DrawNestedScopeBoundaryPorts(context, nestedLayout, scope);
        IReadOnlyList<ChildNodeLayout> childLayouts = [];
        int visibleChildCount = Math.Min(orderedChildren.Count, CompactLayout ? 5 : 8);
        if (visibleChildCount > 0)
        {
            List<ChildNodeLayout> layouts = [];
            for (int index = 0; index < visibleChildCount; index++)
            {
                layouts.Add(DrawScopeNodeCard(context, orderedChildren[index], nestedLayout.ChildNodeRects[index], role: "child"));
            }

            childLayouts = layouts;
        }

        int visibleLocalCount = Math.Min(scope.LocalSignals.Count, CompactLayout ? 8 : 14);
        double localsTop = nestedLayout.LocalSectionRect?.Y ?? (GetLayoutContentBottom(nestedLayout) + 18);
        IReadOnlyDictionary<string, LocalSignalAnchor> localAnchors = DrawNestedLocalSignalSection(context, nestedLayout, scope.LocalSignals, visibleLocalCount, localsTop);
        DrawConnectionRoutes(context, boundaryLayout, childLayouts, localAnchors);

        boundaryLayout = DrawNestedScopeBoundaryPorts(context, nestedLayout, scope);
        for (int index = 0; index < childLayouts.Count; index++)
        {
            DrawScopeNodeCard(context, childLayouts[index].Instance, childLayouts[index].Bounds, role: "child");
        }

        DrawNestedLocalSignalSection(context, nestedLayout, scope.LocalSignals, visibleLocalCount, localsTop);

        if (scope.ChildInstances.Count > visibleChildCount)
        {
            DrawText(
                context,
                $"+{scope.ChildInstances.Count - visibleChildCount} child instances hidden at this zoom.",
                rect.X + 14,
                rect.Bottom - 18,
                Palette.Muted,
                9);
        }

        _scopeHitTargets.Add(new ScopeHitTarget(scope.HierarchyPath, rect, CanExpand: true));
        Dictionary<string, Point> externalAnchors = boundaryLayout.PortAnchors.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ExternalPoint,
            StringComparer.OrdinalIgnoreCase);
        return new ChildNodeLayout(scope, rect, externalAnchors);
    }

    private CurrentPortLayout DrawNestedScopeBoundaryPorts(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        HierarchyScopeInstanceViewModel scope)
    {
        Rect rect = layout.CurrentNodeRect;
        IReadOnlyDictionary<string, PortAnchor> anchors = DrawScopeBoundaryPortGlyphs(context, layout.PanelRect, rect, scope.Ports, attachToEdges: true);
        return new CurrentPortLayout(layout, rect, anchors);
    }

    private IReadOnlyDictionary<string, LocalSignalAnchor> DrawNestedLocalSignalSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        int visibleLocalCount,
        double top)
    {
        Dictionary<string, LocalSignalAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        if (visibleLocalCount == 0)
        {
            return anchors;
        }

        Rect panel = layout.PanelRect;
        DrawText(context, "Local nets", panel.X + 14, top, Palette.Muted, 9);
        double chipWidth = CompactLayout ? 132 : 164;
        double chipHeight = CompactLayout ? 16 : 18;
        double gap = CompactLayout ? 8 : 10;
        int columns = Math.Max(1, Math.Min(3, (int)((panel.Width - 28 + gap) / (chipWidth + gap))));
        for (int index = 0; index < visibleLocalCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            HierarchyScopeLocalSignalViewModel signal = localSignals[index];
            Rect chip = new(
                panel.X + 14 + column * (chipWidth + gap),
                top + 16 + row * (chipHeight + 6),
                chipWidth,
                chipHeight);
            IBrush stroke = signal.IsTraced ? Palette.PinStroke : Palette.ModuleStroke;
            context.FillRectangle(Palette.ValueFill, chip, 4);
            context.DrawRectangle(new Pen(stroke, 1), chip, 4);
            DrawText(context, Ellipsize(signal.Name, 9, chip.Width - 48), chip.X + 6, chip.Y + 2, Palette.Text, 9);
            DrawText(context, signal.WidthLabel, chip.Right - 28, chip.Y + 2, stroke, 9);
            if (!string.IsNullOrWhiteSpace(signal.ResolvedSignalName))
            {
                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signal.ResolvedSignalName!, chip, null));
            }

            anchors[signal.Name] = new LocalSignalAnchor(new Point(chip.Right, chip.Y + chip.Height / 2), signal.ResolvedSignalName, signal.CurrentValue);
        }

        return anchors;
    }

    private IReadOnlyDictionary<string, PortAnchor> DrawScopeBoundaryPortGlyphs(
        DrawingContext context,
        Rect panelRect,
        Rect nodeRect,
        IReadOnlyList<HierarchyScopePortViewModel> ports,
        bool attachToEdges = false)
    {
        Dictionary<string, PortAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        int maxPorts = CompactLayout ? 5 : 8;
        IReadOnlyList<HierarchyScopePortViewModel> inputs = ports.Where(static port => port.IsInput).Take(maxPorts).ToList();
        IReadOnlyList<HierarchyScopePortViewModel> outputs = ports.Where(static port => port.IsOutput).Take(maxPorts).ToList();
        double topInset = attachToEdges ? 16 : (CompactLayout ? 68 : 78);
        double bottomInset = CompactLayout ? 20 : 24;
        double usableHeight = Math.Max(24, nodeRect.Height - topInset - bottomInset);
        double leftStep = usableHeight / Math.Max(1, inputs.Count + 1);
        double rightStep = usableHeight / Math.Max(1, outputs.Count + 1);
        double leftX = nodeRect.X;
        double outputGlyphWidth = CompactLayout ? 18 : 22;
        double rightX = attachToEdges
            ? nodeRect.Right - outputGlyphWidth
            : Math.Min(panelRect.Right - (CompactLayout ? 150 : 190), nodeRect.Right + layoutSafeGap(nodeRect.Width));

        for (int index = 0; index < inputs.Count; index++)
        {
            HierarchyScopePortViewModel port = inputs[index];
            double y = nodeRect.Y + topInset + leftStep * (index + 1);
            anchors[port.Name] = DrawBoundaryPortGlyph(context, port, new Point(leftX, y), isInput: true, attachToEdge: attachToEdges);
        }

        for (int index = 0; index < outputs.Count; index++)
        {
            HierarchyScopePortViewModel port = outputs[index];
            double y = nodeRect.Y + topInset + rightStep * (index + 1);
            anchors[port.Name] = DrawBoundaryPortGlyph(context, port, new Point(rightX, y), isInput: false, attachToEdge: attachToEdges);
        }

        return anchors;

        static double layoutSafeGap(double width) => Math.Max(220, width + 120);
    }

    private PortAnchor DrawBoundaryPortGlyph(DrawingContext context, HierarchyScopePortViewModel port, Point origin, bool isInput, bool attachToEdge)
    {
        double y = origin.Y;
        double glyphWidth = CompactLayout ? 18 : 22;
        double glyphHeight = CompactLayout ? 12 : 14;
        double badgeWidth = CompactLayout ? 48 : 58;
        double labelWidth = CompactLayout ? 78 : 112;
        Rect badge;
        Rect labelRect;
        Point anchor;
        Point externalAnchor;
        Point[] points;
        if (isInput)
        {
            points =
            [
                new Point(origin.X, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth * 0.68, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth, y),
                new Point(origin.X + glyphWidth * 0.68, y + glyphHeight / 2),
                new Point(origin.X, y + glyphHeight / 2)
            ];
            badge = new Rect(origin.X + glyphWidth + 8, y - 10, badgeWidth, 20);
            labelRect = new Rect(badge.Right + 8, y - 18, labelWidth, 18);
            anchor = new Point(labelRect.Right + 8, y);
            externalAnchor = new Point(origin.X, y);
            context.DrawLine(new Pen(Palette.PinStroke, 1.2), new Point(origin.X + glyphWidth, y), anchor);
        }
        else
        {
            points =
            [
                new Point(origin.X + glyphWidth, y - glyphHeight / 2),
                new Point(origin.X + glyphWidth * 0.32, y - glyphHeight / 2),
                new Point(origin.X, y),
                new Point(origin.X + glyphWidth * 0.32, y + glyphHeight / 2),
                new Point(origin.X + glyphWidth, y + glyphHeight / 2)
            ];
            double outputLabelWidth = attachToEdge
                ? Math.Min(labelWidth, Math.Max(34, MeasureWidth(port.Name, 10) + 4))
                : labelWidth;
            badge = attachToEdge
                ? new Rect(origin.X - badgeWidth - 8, y - 10, badgeWidth, 20)
                : default;
            labelRect = attachToEdge
                ? new Rect(badge.X - outputLabelWidth - 8, y - 18, outputLabelWidth, 18)
                : new Rect(origin.X - labelWidth - badgeWidth - 18, y - 18, labelWidth, 18);
            if (!attachToEdge)
            {
                badge = new Rect(labelRect.Right + 8, y - 10, badgeWidth, 20);
            }

            anchor = attachToEdge
                ? new Point(badge.X - 8, y)
                : new Point(labelRect.X - 8, y);
            externalAnchor = new Point(origin.X + glyphWidth, y);
            if (attachToEdge)
            {
                context.DrawLine(new Pen(Palette.OutputValue, 1.2), anchor, new Point(badge.X, y));
                context.DrawLine(new Pen(Palette.OutputValue, 1.2), new Point(badge.Right, y), new Point(origin.X, y));
            }
            else
            {
                context.DrawLine(new Pen(Palette.OutputValue, 1.2), anchor, new Point(origin.X, y));
            }
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], isFilled: true);
            for (int index = 1; index < points.Length; index++)
            {
                geometryContext.LineTo(points[index]);
            }

            geometryContext.EndFigure(isClosed: true);
        }

        IBrush stroke = isInput ? Palette.PinStroke : Palette.OutputValue;
        context.DrawGeometry(Palette.ValueFill, new Pen(stroke, 1.1), geometry);
        DrawMiniBadge(context, badge, port.WidthLabel, Palette.PinStroke);
        DrawText(context, Ellipsize(port.Name, 10, labelRect.Width), labelRect.X, labelRect.Y + 2, Palette.Muted, 10);
        context.FillRectangle(stroke, new Rect(anchor.X - 2, anchor.Y - 2, 4, 4));
        context.FillRectangle(stroke, new Rect(externalAnchor.X - 2, externalAnchor.Y - 2, 4, 4));
        return new PortAnchor(port.Name, anchor, port.IsInput, externalAnchor);
    }

    private IReadOnlyDictionary<string, Point> DrawChildConnectionStubs(
        DrawingContext context,
        HierarchyScopeInstanceViewModel scope,
        SchematicNodeCardLayout layout,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> inputs,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> outputs)
    {
        Dictionary<string, Point> anchors = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < inputs.Count && index < layout.InputRows.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = inputs[index];
            SchematicNodeCardRowLayout row = layout.InputRows[index];
            DrawNodeConnectionRow(context, row, connection.PortName, connection.WidthLabel, Palette.PinStroke, Palette.Text);
            anchors[connection.PortName] = row.RouteAnchor;
        }

        for (int index = 0; index < outputs.Count && index < layout.OutputRows.Count; index++)
        {
            HierarchyScopeInstancePortConnectionViewModel connection = outputs[index];
            SchematicNodeCardRowLayout row = layout.OutputRows[index];
            DrawNodeConnectionRow(context, row, connection.PortName, connection.WidthLabel, Palette.OutputValue, Palette.Text);
            anchors[connection.PortName] = row.RouteAnchor;
        }

        if (layout.HiddenInputCount > 0)
        {
            DrawText(context, $"+{layout.HiddenInputCount} in", layout.FooterRect.X + 8, layout.FooterRect.Y + 3, Palette.Muted, 9);
        }

        if (layout.HiddenOutputCount > 0)
        {
            string hiddenText = $"+{layout.HiddenOutputCount} out";
            DrawText(
                context,
                hiddenText,
                layout.FooterRect.Right - MeasureWidth(hiddenText, 9) - 8,
                layout.FooterRect.Y + 3,
                Palette.Muted,
                9);
        }

        return anchors;
    }

    private void DrawNodeConnectionRow(
        DrawingContext context,
        SchematicNodeCardRowLayout row,
        string label,
        string widthLabel,
        IBrush stroke,
        IBrush textBrush)
    {
        context.DrawLine(new Pen(stroke, 1.15), row.StubStart, row.StubEnd);
        context.FillRectangle(stroke, new Rect(row.RouteAnchor.X - 2, row.RouteAnchor.Y - 2, 4, 4));
        string displayLabel = widthLabel == "1b" ? label : $"{label} [{widthLabel}]";
        string renderedLabel = Ellipsize(displayLabel, 9, row.TextBandRect.Width);
        double labelWidth = MeasureWidth(renderedLabel, 9);
        double labelX = row.IsInput
            ? row.TextBandRect.X
            : Math.Max(row.TextBandRect.X, row.StubStart.X - labelWidth - 8);
        double labelY = row.RouteAnchor.Y - 7;
        DrawText(
            context,
            renderedLabel,
            labelX,
            labelY,
            textBrush,
            9);
    }

    private IReadOnlyDictionary<string, LocalSignalAnchor> DrawLocalSignalSection(
        DrawingContext context,
        SchematicScopePanelLayout layout,
        IReadOnlyList<HierarchyScopeLocalSignalViewModel> localSignals,
        int visibleLocalCount,
        double top)
    {
        Rect panel = layout.PanelRect;
        Dictionary<string, LocalSignalAnchor> anchors = new(StringComparer.OrdinalIgnoreCase);
        if (visibleLocalCount == 0)
        {
            return anchors;
        }

        DrawText(context, "Local nets", panel.X + 16, top, Palette.Text, 11);
        double chipY = top + 18;
        double chipWidth = panel.Width >= 760 && visibleLocalCount > 2 ? (CompactLayout ? 150 : 190) : (CompactLayout ? 170 : 220);
        int columns = Math.Max(1, layout.LocalColumns);
        for (int index = 0; index < visibleLocalCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            HierarchyScopeLocalSignalViewModel signal = localSignals[index];
            Rect chip = new(
                panel.X + 16 + column * (chipWidth + (CompactLayout ? 12 : 14)),
                chipY + row * (CompactLayout ? 20 : 24),
                chipWidth,
                CompactLayout ? 16 : 18);
            IBrush stroke = signal.IsTraced ? Palette.PinStroke : Palette.ModuleStroke;
            context.FillRectangle(Palette.ValueFill, chip, 4);
            context.DrawRectangle(new Pen(stroke, 1), chip, 4);
            DrawText(context, Ellipsize(signal.Name, 10, chip.Width - 62), chip.X + 6, chip.Y + 2, Palette.Text, 10);
            DrawText(context, signal.WidthLabel, chip.Right - 28, chip.Y + 2, stroke, 10);
            if (!string.IsNullOrWhiteSpace(signal.ResolvedSignalName))
            {
                bool selected = string.Equals(SelectedSignalName, signal.ResolvedSignalName, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    context.DrawRectangle(new Pen(Palette.Selected, 1.2), chip.Inflate(1), 4);
                }

                _signalReferenceHitTargets.Add(new SignalReferenceHitTarget(signal.ResolvedSignalName!, chip, null));
            }

            anchors[signal.Name] = new LocalSignalAnchor(new Point(chip.Right, chip.Y + chip.Height / 2), signal.ResolvedSignalName, signal.CurrentValue);
        }

        return anchors;
    }

    private void DrawPortCountStubs(DrawingContext context, Rect rect, int inputCount, int outputCount, IBrush stroke)
    {
        double leftCount = Math.Min(3, inputCount);
        double rightCount = Math.Min(3, outputCount);
        for (int index = 0; index < leftCount; index++)
        {
            double y = rect.Y + 12 + index * 10;
            context.DrawLine(new Pen(stroke, 1.1), new Point(rect.X - 8, y), new Point(rect.X, y));
        }

        for (int index = 0; index < rightCount; index++)
        {
            double y = rect.Y + 12 + index * 10;
            context.DrawLine(new Pen(stroke, 1.1), new Point(rect.Right, y), new Point(rect.Right + 8, y));
        }
    }

    private void DrawScopeExpansionButton(DrawingContext context, Rect ownerRect, string? hierarchyPath, bool expanded)
    {
        if (string.IsNullOrWhiteSpace(hierarchyPath))
        {
            return;
        }

        Rect button = new(ownerRect.Right - 28, ownerRect.Y + 10, 18, 18);
        IBrush stroke = expanded ? Palette.Selected : Palette.PinStroke;
        context.FillRectangle(Palette.ValueFill, button, 4);
        context.DrawRectangle(new Pen(stroke, 1.1), button, 4);
        DrawText(context, expanded ? "-" : "+", button.X + 5, button.Y + 1, stroke, 13);
        _expansionHitTargets.Add(new ExpansionHitTarget(hierarchyPath, button.Inflate(4)));
    }

    private static void DrawValueBadge(DrawingContext context, string value, Rect rect, IBrush strokeBrush, IBrush fillBrush)
    {
        context.FillRectangle(fillBrush, rect, 5);
        context.DrawRectangle(new Pen(strokeBrush, 1), rect, 5);
        DrawText(
            context,
            value,
            rect.X + Math.Max(8, (rect.Width - MeasureWidth(value, 11)) / 2),
            rect.Y + 5,
            strokeBrush,
            11);
    }

    private void DrawMiniBadge(DrawingContext context, Rect rect, string text, IBrush strokeBrush)
    {
        context.FillRectangle(Palette.ValueFill, rect, 4);
        context.DrawRectangle(new Pen(strokeBrush, 1), rect, 4);
        DrawText(context, Ellipsize(text, 10, rect.Width - 8), rect.X + 4, rect.Y + 2, strokeBrush, 10);
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

    private static double MeasureWidth(string text, double size)
    {
        FormattedText formatted = new(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            Brushes.Black);
        return formatted.Width;
    }

    private static string Ellipsize(string text, double size, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return string.Empty;
        }

        if (MeasureWidth(text, size) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        int length = text.Length;
        while (length > 1)
        {
            string candidate = text[..length] + ellipsis;
            if (MeasureWidth(candidate, size) <= maxWidth)
            {
                return candidate;
            }

            length--;
        }

        return ellipsis;
    }

    private static double GetValueBadgeWidth(string value, double minWidth, double maxWidth) =>
        Math.Clamp(MeasureWidth(value, 11) + 18, minWidth, maxWidth);
}
