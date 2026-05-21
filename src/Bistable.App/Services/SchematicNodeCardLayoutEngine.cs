using Avalonia;

namespace Bistable.App.Services;

public sealed class SchematicNodeCardLayoutEngine
{
    public SchematicNodeCardLayout Compute(SchematicNodeCardLayoutInput input)
    {
        double headerHeight = input.CompactLayout ? 34 : 40;
        double footerHeight = input.CompactLayout ? 18 : 22;
        double bodyTop = input.Bounds.Y + headerHeight;
        double footerTop = input.Bounds.Bottom - footerHeight;
        Rect headerRect = new(input.Bounds.X, input.Bounds.Y, input.Bounds.Width, headerHeight);
        Rect bodyRect = new(input.Bounds.X, bodyTop, input.Bounds.Width, Math.Max(18, footerTop - bodyTop));
        Rect footerRect = new(input.Bounds.X, footerTop, input.Bounds.Width, footerHeight);

        IReadOnlyList<SchematicNodeCardRowLayout> inputRows = BuildRows(
            input.Bounds,
            bodyRect,
            input.InputCount,
            isInput: true,
            input.CompactLayout);
        IReadOnlyList<SchematicNodeCardRowLayout> outputRows = BuildRows(
            input.Bounds,
            bodyRect,
            input.OutputCount,
            isInput: false,
            input.CompactLayout);

        return new SchematicNodeCardLayout(
            input.Bounds,
            headerRect,
            bodyRect,
            footerRect,
            inputRows,
            outputRows,
            Math.Max(0, input.TotalInputCount - inputRows.Count),
            Math.Max(0, input.TotalOutputCount - outputRows.Count));
    }

    private static IReadOnlyList<SchematicNodeCardRowLayout> BuildRows(
        Rect cardRect,
        Rect bodyRect,
        int visibleCount,
        bool isInput,
        bool compactLayout)
    {
        if (visibleCount == 0)
        {
            return [];
        }

        double sideInset = compactLayout ? 12 : 14;
        double rowHeight = compactLayout ? 16 : 18;
        double rowGap = compactLayout ? 5 : 6;
        double availableHeight = Math.Max(12, bodyRect.Height - 8);
        double preferredHeight = visibleCount * rowHeight + Math.Max(0, visibleCount - 1) * rowGap;
        if (preferredHeight > availableHeight)
        {
            rowGap = visibleCount <= 1
                ? 0
                : Math.Max(1, (availableHeight - visibleCount * (compactLayout ? 10 : 12)) / (visibleCount - 1));
            double maxRowHeight = (availableHeight - Math.Max(0, visibleCount - 1) * rowGap) / visibleCount;
            rowHeight = Math.Max(compactLayout ? 10 : 12, maxRowHeight);
        }

        double totalHeight = visibleCount * rowHeight + Math.Max(0, visibleCount - 1) * rowGap;
        double startY = bodyRect.Y + Math.Max(4, (bodyRect.Height - totalHeight) / 2);
        double stubLength = compactLayout ? 12 : 14;
        double pinGap = compactLayout ? 8 : 10;
        double widthBadgeWidth = compactLayout ? 28 : 32;
        double textBandWidth = Math.Min(
            Math.Max(compactLayout ? 96 : 122, cardRect.Width * (compactLayout ? 0.38 : 0.42)),
            Math.Max(76, cardRect.Width / 2 - sideInset - 10));

        List<SchematicNodeCardRowLayout> rows = [];
        for (int index = 0; index < visibleCount; index++)
        {
            double y = startY + index * (rowHeight + rowGap);
            Rect rowRect = new(cardRect.X + sideInset, y, cardRect.Width - sideInset * 2, rowHeight);
            Point stubStart = isInput
                ? new Point(cardRect.X, y + rowHeight / 2)
                : new Point(cardRect.Right - stubLength, y + rowHeight / 2);
            Point stubEnd = isInput
                ? new Point(cardRect.X + stubLength, y + rowHeight / 2)
                : new Point(cardRect.Right, y + rowHeight / 2);
            Rect textBand = isInput
                ? new Rect(cardRect.X + stubLength + pinGap, y + 1, textBandWidth, rowHeight - 2)
                : new Rect(cardRect.Right - stubLength - pinGap - textBandWidth, y + 1, textBandWidth, rowHeight - 2);
            Rect widthBadge = isInput
                ? new Rect(textBand.X, y + 1, widthBadgeWidth, rowHeight - 2)
                : new Rect(textBand.Right - widthBadgeWidth, y + 1, widthBadgeWidth, rowHeight - 2);
            Rect labelRect = isInput
                ? new Rect(textBand.X + widthBadgeWidth + 6, y + 1, Math.Max(24, textBand.Width - widthBadgeWidth - 8), rowHeight - 2)
                : new Rect(textBand.X, y + 1, Math.Max(24, textBand.Width - widthBadgeWidth - 8), rowHeight - 2);
            Point routeAnchor = isInput ? new(cardRect.X, y + rowHeight / 2) : new(cardRect.Right, y + rowHeight / 2);

            rows.Add(new SchematicNodeCardRowLayout(
                rowRect,
                textBand,
                labelRect,
                widthBadge,
                stubStart,
                stubEnd,
                routeAnchor,
                isInput));
        }

        return rows;
    }
}

public sealed record SchematicNodeCardLayoutInput(
    Rect Bounds,
    bool CompactLayout,
    int InputCount,
    int OutputCount,
    int TotalInputCount,
    int TotalOutputCount);

public sealed record SchematicNodeCardLayout(
    Rect Bounds,
    Rect HeaderRect,
    Rect BodyRect,
    Rect FooterRect,
    IReadOnlyList<SchematicNodeCardRowLayout> InputRows,
    IReadOnlyList<SchematicNodeCardRowLayout> OutputRows,
    int HiddenInputCount,
    int HiddenOutputCount);

public sealed record SchematicNodeCardRowLayout(
    Rect Bounds,
    Rect TextBandRect,
    Rect LabelRect,
    Rect WidthBadgeRect,
    Point StubStart,
    Point StubEnd,
    Point RouteAnchor,
    bool IsInput);
