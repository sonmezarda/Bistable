using Avalonia;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class SchematicScopeLayoutEngineTests
{
    private readonly SchematicScopeLayoutEngine _engine = new();

    [Fact]
    public void DenseNonCompactLayoutSeparatesCurrentNodeChildrenAndSections()
    {
        SchematicScopePanelLayout layout = _engine.Compute(new SchematicScopeLayoutInput(
            new Rect(0, 0, 2600, 1800),
            new Rect(920, 180, 520, 340),
            112,
            CompactLayout: false,
            ParentVisible: true,
            ScopeSignalCount: 18,
            ChildScopeCount: 8,
            LocalSignalCount: 8,
            InputPortCount: 8,
            OutputPortCount: 6,
            MaxChildConnectionRows: 5));

        Assert.True(layout.PanelRect.Contains(layout.CurrentNodeRect));
        Assert.NotNull(layout.ParentNodeRect);
        Assert.Equal(8, layout.ChildNodeRects.Count);
        Assert.True(layout.RouteCorridorWidth >= 100);
        Assert.True(layout.InlineChildren);

        AssertNoIntersections(layout.ChildNodeRects);
        Assert.All(layout.ChildNodeRects, rect =>
        {
            Assert.True(rect.X >= layout.CurrentNodeRect.Right + 96);
            Assert.True(rect.Bottom <= layout.LocalSectionRect!.Value.Y - 20);
        });

        Assert.True(layout.ParentNodeRect!.Value.Right < layout.CurrentNodeRect.X);
        Assert.True(layout.LocalSectionRect!.Value.Y > layout.CurrentNodeRect.Bottom);
        Assert.True(layout.ProbeSectionRect.Y > layout.LocalSectionRect!.Value.Bottom);
    }

    [Fact]
    public void CompactLayoutFallsBackToBelowChildRowsWithoutOverlap()
    {
        SchematicScopePanelLayout layout = _engine.Compute(new SchematicScopeLayoutInput(
            new Rect(0, 0, 920, 1200),
            new Rect(260, 120, 400, 260),
            96,
            CompactLayout: true,
            ParentVisible: true,
            ScopeSignalCount: 6,
            ChildScopeCount: 4,
            LocalSignalCount: 4,
            InputPortCount: 5,
            OutputPortCount: 3,
            MaxChildConnectionRows: 3));

        Assert.Equal(4, layout.ChildNodeRects.Count);
        Assert.False(layout.InlineChildren);
        AssertNoIntersections(layout.ChildNodeRects);
        Assert.All(layout.ChildNodeRects, rect => Assert.True(rect.Y >= layout.CurrentNodeRect.Bottom + 18));
        Assert.True(layout.LocalSectionRect!.Value.Y > layout.ChildNodeRects.Max(rect => rect.Bottom));
        Assert.True(layout.ProbeSectionRect.Y > layout.LocalSectionRect!.Value.Bottom);
    }

    private static void AssertNoIntersections(IReadOnlyList<Rect> rects)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            for (int j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].Intersects(rects[j]), $"Rects {i} and {j} intersect: {rects[i]} vs {rects[j]}");
            }
        }
    }
}
