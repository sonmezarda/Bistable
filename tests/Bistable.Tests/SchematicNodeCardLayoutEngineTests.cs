using Avalonia;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class SchematicNodeCardLayoutEngineTests
{
    private readonly SchematicNodeCardLayoutEngine _engine = new();

    [Fact]
    public void NonCompactLayoutSeparatesHeaderBodyFooterAndRows()
    {
        SchematicNodeCardLayout layout = _engine.Compute(new SchematicNodeCardLayoutInput(
            new Rect(0, 0, 260, 164),
            CompactLayout: false,
            InputCount: 5,
            OutputCount: 4,
            TotalInputCount: 7,
            TotalOutputCount: 6));

        Assert.True(layout.HeaderRect.Bottom <= layout.BodyRect.Y);
        Assert.True(layout.BodyRect.Bottom <= layout.FooterRect.Y);
        Assert.Equal(5, layout.InputRows.Count);
        Assert.Equal(4, layout.OutputRows.Count);
        Assert.Equal(2, layout.HiddenInputCount);
        Assert.Equal(2, layout.HiddenOutputCount);
        AssertRowsInsideBody(layout.BodyRect, layout.InputRows);
        AssertRowsInsideBody(layout.BodyRect, layout.OutputRows);
        AssertNoRowOverlap(layout.InputRows);
        AssertNoRowOverlap(layout.OutputRows);
    }

    [Fact]
    public void CompactLayoutKeepsTextBandsInsideCardBounds()
    {
        SchematicNodeCardLayout layout = _engine.Compute(new SchematicNodeCardLayoutInput(
            new Rect(120, 80, 196, 96),
            CompactLayout: true,
            InputCount: 3,
            OutputCount: 2,
            TotalInputCount: 3,
            TotalOutputCount: 2));

        Assert.All(layout.InputRows.Concat(layout.OutputRows), row =>
        {
            Assert.True(layout.Bounds.Contains(row.TextBandRect.TopLeft));
            Assert.True(layout.Bounds.Contains(row.TextBandRect.BottomRight));
            Assert.True(layout.Bounds.Contains(row.LabelRect.TopLeft));
            Assert.True(layout.Bounds.Contains(row.LabelRect.BottomRight));
            Assert.True(layout.Bounds.Contains(row.WidthBadgeRect.TopLeft));
            Assert.True(layout.Bounds.Contains(row.WidthBadgeRect.BottomRight));
        });
    }

    private static void AssertRowsInsideBody(Rect bodyRect, IReadOnlyList<SchematicNodeCardRowLayout> rows)
    {
        Assert.All(rows, row =>
        {
            Assert.True(row.Bounds.Y >= bodyRect.Y);
            Assert.True(row.Bounds.Bottom <= bodyRect.Bottom);
        });
    }

    private static void AssertNoRowOverlap(IReadOnlyList<SchematicNodeCardRowLayout> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            for (int j = i + 1; j < rows.Count; j++)
            {
                Assert.False(rows[i].Bounds.Intersects(rows[j].Bounds), $"Rows {i} and {j} overlap.");
            }
        }
    }
}
