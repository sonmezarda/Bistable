using Avalonia;

namespace Bistable.App.Services;

public sealed class SchematicScopeLayoutEngine
{
    public SchematicScopePanelLayout Compute(SchematicScopeLayoutInput input)
    {
        int visibleProbeCount = Math.Min(input.ScopeSignalCount, input.CompactLayout ? 8 : 14);
        int visibleChildCount = Math.Min(input.ChildScopeCount, input.CompactLayout ? 4 : 8);
        int visibleLocalCount = Math.Min(input.LocalSignalCount, input.CompactLayout ? 4 : 8);
        int visibleLeftPortCount = Math.Min(input.InputPortCount, input.CompactLayout ? 5 : 8);
        int visibleRightPortCount = Math.Min(input.OutputPortCount, input.CompactLayout ? 5 : 8);
        bool inlineChildren = visibleChildCount > 0 && input.WorldBounds.Width >= (input.CompactLayout ? 980 : 1400);

        int childColumnCount = inlineChildren && visibleChildCount > 2 ? 2 : 1;
        int childRowCount = visibleChildCount == 0 ? 0 : (int)Math.Ceiling(visibleChildCount / (double)childColumnCount);
        int probeColumnCount = input.WorldBounds.Width >= (input.CompactLayout ? 900 : 1200) && visibleProbeCount > 1
            ? (input.CompactLayout ? 2 : Math.Min(3, visibleProbeCount))
            : 1;
        int probeRowCount = visibleProbeCount == 0 ? 0 : (int)Math.Ceiling(visibleProbeCount / (double)probeColumnCount);

        double panelWidth = Math.Clamp(
            input.WorldBounds.Width * (inlineChildren ? (input.CompactLayout ? 0.72 : 0.78) : (input.CompactLayout ? 0.58 : 0.66)),
            input.CompactLayout ? 360 : 620,
            inlineChildren ? (input.CompactLayout ? 760 : 1240) : (input.CompactLayout ? 620 : 980));

        double titleBlockHeight = input.CompactLayout ? 74 : 94;
        double childCardHeight = GetChildCardHeight(input.MaxChildConnectionRows, input.CompactLayout);
        double childRowPitch = childCardHeight + (input.CompactLayout ? 10 : 16);
        double navigationHeight = (input.CompactLayout ? 126 : 162) + (inlineChildren ? Math.Max(0, childRowCount - 1) * childRowPitch : 0);
        double childrenBlockHeight = inlineChildren || visibleChildCount == 0
            ? 0
            : (input.CompactLayout ? 68 : 88) + Math.Max(0, childRowCount - 1) * childRowPitch;
        int localColumns = !input.CompactLayout && panelWidth >= 900 && visibleLocalCount > 2 ? 2 : 1;
        int localRowCount = visibleLocalCount == 0 ? 0 : (int)Math.Ceiling(visibleLocalCount / (double)localColumns);
        double localBlockHeight = visibleLocalCount == 0
            ? 0
            : (input.CompactLayout ? 50 : 62) + Math.Max(0, localRowCount - 1) * (input.CompactLayout ? 20 : 26);
        double probeBlockHeight = visibleProbeCount == 0
            ? 46
            : (input.CompactLayout ? 38 : 48) + probeRowCount * (input.CompactLayout ? 30 : 36);
        double panelHeight = Math.Clamp(
            navigationHeight + childrenBlockHeight + localBlockHeight + probeBlockHeight + (input.CompactLayout ? 72 : 96),
            input.CompactLayout ? 240 : 360,
            input.CompactLayout ? 430 : 700);

        double panelX = Math.Clamp(
            input.ModuleRect.Center.X - panelWidth / 2,
            16,
            input.WorldBounds.Right - panelWidth - 16);
        double minimumTop = input.ScopeCardBottom is null ? 18 : input.ScopeCardBottom.Value + 12;
        double targetY = input.ModuleRect.Bottom + 38;
        double panelY = Math.Max(minimumTop, Math.Min(input.WorldBounds.Bottom - panelHeight - 16, targetY));
        Rect panel = new(panelX, panelY, panelWidth, panelHeight);

        double currentNodeWidth = input.CompactLayout ? 220 : 320;
        double currentNodeHeight = GetCurrentNodeHeight(visibleLeftPortCount, visibleRightPortCount, input.CompactLayout);
        double headerBottom = panel.Y + titleBlockHeight;
        double currentNodeY = headerBottom + (input.CompactLayout ? 8 : 14);

        double parentWidth = input.ParentVisible ? (input.CompactLayout ? 128 : 152) : 0;
        double parentGap = input.ParentVisible ? (input.CompactLayout ? 18 : 26) : 0;
        double leftPortZone = visibleLeftPortCount > 0 ? (input.CompactLayout ? 184 : 240) : 28;
        double rightPortZone = visibleRightPortCount > 0 ? (input.CompactLayout ? 184 : 240) : 28;
        double routeCorridorWidth = visibleChildCount > 0 ? (input.CompactLayout ? 82 : 132) : (input.CompactLayout ? 28 : 36);
        double outerMargin = 16;
        double rightReserved = outerMargin + rightPortZone;

        double childAreaX = 0;
        double childCardWidth = 0;
        IReadOnlyList<Rect> childRects = [];
        if (visibleChildCount > 0 && inlineChildren)
        {
            double childGap = input.CompactLayout ? 10 : 18;
            double availableForCurrentAndChildren = panel.Width - outerMargin - Math.Max(leftPortZone, parentWidth + parentGap) - rightReserved;
            double preferredChildWidth = input.CompactLayout ? 196 : 262;
            double neededWidth = currentNodeWidth + routeCorridorWidth + childColumnCount * preferredChildWidth + (childColumnCount - 1) * childGap;
            double shrink = Math.Max(0, neededWidth - availableForCurrentAndChildren);
            currentNodeWidth = Math.Max(input.CompactLayout ? 188 : 240, currentNodeWidth - shrink * 0.25);
            double childAvailableWidth = Math.Max(
                childColumnCount == 1 ? 180 : 2 * (input.CompactLayout ? 164 : 216) + childGap,
                availableForCurrentAndChildren - currentNodeWidth - routeCorridorWidth);
            childCardWidth = childColumnCount == 1
                ? Math.Min(Math.Max(180, childAvailableWidth), input.CompactLayout ? 280 : 360)
                : (childAvailableWidth - childGap) / 2;

            double currentNodeX = panel.X + outerMargin + Math.Max(leftPortZone, parentWidth + parentGap);
            Rect currentRect = new(currentNodeX, currentNodeY, currentNodeWidth, currentNodeHeight);
            childAreaX = currentRect.Right + routeCorridorWidth;
            childRects = BuildChildRects(
                childAreaX,
                currentRect.Y,
                childCardWidth,
                childCardHeight,
                childColumnCount,
                visibleChildCount,
                input.CompactLayout ? 10 : 18,
                childRowPitch);

            return FinalizeLayout(
                input,
                panel,
                currentRect,
                childRects,
                routeCorridorWidth,
                inlineChildren,
                titleBlockHeight,
                childrenBlockHeight,
                localBlockHeight,
                probeBlockHeight,
                visibleLocalCount,
                visibleProbeCount,
                localColumns,
                localRowCount,
                probeColumnCount,
                probeRowCount,
                childCardWidth,
                childCardHeight);
        }

        double currentNodeXCentered = panel.X + Math.Max(leftPortZone, outerMargin)
            + Math.Max(0, panel.Width - Math.Max(leftPortZone, outerMargin) - rightReserved - currentNodeWidth) / 2;
        Rect centeredCurrentRect = new(currentNodeXCentered, currentNodeY, currentNodeWidth, currentNodeHeight);
        childRects = BuildChildRectsBelow(
            panel,
            centeredCurrentRect,
            visibleChildCount,
            childCardHeight,
            input.CompactLayout);

        return FinalizeLayout(
            input,
            panel,
            centeredCurrentRect,
            childRects,
            routeCorridorWidth,
            inlineChildren,
            titleBlockHeight,
            childrenBlockHeight,
            localBlockHeight,
            probeBlockHeight,
            visibleLocalCount,
            visibleProbeCount,
            localColumns,
            localRowCount,
            probeColumnCount,
            probeRowCount,
            childRects.FirstOrDefault().Width,
            childCardHeight);
    }

    private static SchematicScopePanelLayout FinalizeLayout(
        SchematicScopeLayoutInput input,
        Rect panel,
        Rect currentRect,
        IReadOnlyList<Rect> childRects,
        double routeCorridorWidth,
        bool inlineChildren,
        double titleBlockHeight,
        double childrenBlockHeight,
        double localBlockHeight,
        double probeBlockHeight,
        int visibleLocalCount,
        int visibleProbeCount,
        int localColumns,
        int localRowCount,
        int probeColumnCount,
        int probeRowCount,
        double childCardWidth,
        double childCardHeight)
    {
        Rect? parentRect = input.ParentVisible
            ? new Rect(panel.X + 16, currentRect.Y + (input.CompactLayout ? 8 : 12), input.CompactLayout ? 128 : 152, input.CompactLayout ? 48 : 60)
            : null;

        double childBottom = childRects.Count == 0 ? currentRect.Bottom : Math.Max(currentRect.Bottom, childRects.Max(rect => rect.Bottom));
        double localsTop = inlineChildren
            ? childBottom + (input.CompactLayout ? 22 : 30)
            : currentRect.Bottom + (input.CompactLayout ? 18 : 28) + childrenBlockHeight;
        Rect? localSection = visibleLocalCount == 0
            ? null
            : new Rect(panel.X + 16, localsTop, panel.Width - 32, localBlockHeight);
        double probesTop = localsTop + localBlockHeight + (localBlockHeight > 0 ? (input.CompactLayout ? 10 : 14) : 0);
        Rect probeSection = new(panel.X + 16, probesTop, panel.Width - 32, probeBlockHeight);

        return new SchematicScopePanelLayout(
            panel,
            currentRect,
            parentRect,
            childRects,
            localSection,
            probeSection,
            inlineChildren,
            routeCorridorWidth,
            childCardWidth,
            childCardHeight,
            titleBlockHeight,
            localColumns,
            localRowCount,
            probeColumnCount,
            probeRowCount);
    }

    private static IReadOnlyList<Rect> BuildChildRects(
        double startX,
        double startY,
        double cardWidth,
        double cardHeight,
        int columns,
        int visibleChildCount,
        double columnGap,
        double rowPitch)
    {
        List<Rect> rects = [];
        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            rects.Add(new Rect(
                startX + column * (cardWidth + columnGap),
                startY + row * rowPitch,
                cardWidth,
                cardHeight));
        }

        return rects;
    }

    private static IReadOnlyList<Rect> BuildChildRectsBelow(
        Rect panel,
        Rect currentRect,
        int visibleChildCount,
        double cardHeight,
        bool compactLayout)
    {
        if (visibleChildCount == 0)
        {
            return [];
        }

        int columns = panel.Width >= 520 && visibleChildCount > 1 ? 2 : 1;
        double gap = compactLayout ? 10 : 18;
        double availableWidth = panel.Width - 32;
        double cardWidth = columns == 1 ? availableWidth : (availableWidth - gap) / 2;
        double top = currentRect.Bottom + (compactLayout ? 18 : 28);
        double rowPitch = cardHeight + (compactLayout ? 10 : 16);

        List<Rect> rects = [];
        for (int index = 0; index < visibleChildCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            rects.Add(new Rect(
                panel.X + 16 + column * (cardWidth + gap),
                top + row * rowPitch,
                cardWidth,
                cardHeight));
        }

        return rects;
    }

    private static double GetCurrentNodeHeight(int inputCount, int outputCount, bool compactLayout)
    {
        int portCount = Math.Max(inputCount, outputCount);
        return compactLayout
            ? Math.Clamp(58 + portCount * 9, 70, 120)
            : Math.Clamp(78 + portCount * 14, 96, 184);
    }

    private static double GetChildCardHeight(int maxChildConnectionRows, bool compactLayout) =>
        compactLayout
            ? Math.Clamp(50 + maxChildConnectionRows * 12, 56, 100)
            : Math.Clamp(78 + maxChildConnectionRows * 16, 92, 168);
}

public sealed record SchematicScopeLayoutInput(
    Rect WorldBounds,
    Rect ModuleRect,
    double? ScopeCardBottom,
    bool CompactLayout,
    bool ParentVisible,
    int ScopeSignalCount,
    int ChildScopeCount,
    int LocalSignalCount,
    int InputPortCount,
    int OutputPortCount,
    int MaxChildConnectionRows);

public sealed record SchematicScopePanelLayout(
    Rect PanelRect,
    Rect CurrentNodeRect,
    Rect? ParentNodeRect,
    IReadOnlyList<Rect> ChildNodeRects,
    Rect? LocalSectionRect,
    Rect ProbeSectionRect,
    bool InlineChildren,
    double RouteCorridorWidth,
    double ChildCardWidth,
    double ChildCardHeight,
    double TitleBlockHeight,
    int LocalColumns,
    int LocalRowCount,
    int ProbeColumns,
    int ProbeRowCount);
