using Avalonia;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class SchematicConnectionRouterTests
{
    private readonly SchematicConnectionRouter _router = new();

    [Fact]
    public void InlineLayoutSeparatesInputAndOutputConnectionLanes()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(300, 180, 280, 156),
            null,
            [
                new Rect(760, 180, 220, 120),
                new Rect(760, 336, 220, 120)
            ],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("in-a", "clk", "clk", 1, new Point(580, 220), new Point(760, 212), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("in-b", "rst_n", "rst_n", 1, new Point(580, 262), new Point(760, 378), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("out-a", "sum", "sum", 8, new Point(580, 240), new Point(980, 244), SourceFromLocalSignal: false, TargetIsInput: false),
                new SchematicConnectionRouteRequest("out-b", "carry", "carry", 1, new Point(260, 552), new Point(980, 398), SourceFromLocalSignal: true, TargetIsInput: false)
            ]));

        SchematicConnectionRoute[] inputRoutes = routes.Where(static route => route.Id.StartsWith("in-", StringComparison.OrdinalIgnoreCase)).ToArray();
        SchematicConnectionRoute[] outputRoutes = routes.Where(static route => route.Id.StartsWith("out-", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Equal(2, inputRoutes.Length);
        Assert.Equal(2, outputRoutes.Length);
        Assert.All(inputRoutes, route =>
        {
            double laneX = route.Points[1].X;
            Assert.True(laneX > layout.CurrentNodeRect.Right);
            Assert.True(laneX < layout.ChildNodeRects.Min(static rect => rect.X));
            Assert.Equal(laneX, route.Points[2].X);
        });

        Assert.All(outputRoutes, route =>
        {
            double laneX = route.Points[^2].X;
            Assert.True(laneX > layout.ChildNodeRects.Max(static rect => rect.Right));
            Assert.Equal(laneX, route.Points[^3].X);
        });

        Assert.Equal(2, inputRoutes.Select(static route => route.Points[1].X).Distinct().Count());
        Assert.Equal(2, outputRoutes.Select(static route => route.Points[^2].X).Distinct().Count());
        Assert.All(outputRoutes.Where(static route => route.Id == "out-a"), route =>
            Assert.True(route.Points[2].Y < layout.ChildNodeRects.Min(static rect => rect.Y)));
        Assert.All(routes, route => Assert.True(route.LabelBounds.Width > 0));
    }

    [Fact]
    public void StackedLayoutUsesUpperLanesForCurrentAndLowerLanesForLocalSources()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 980, 940),
            new Rect(320, 180, 240, 140),
            null,
            [
                new Rect(180, 400, 260, 120),
                new Rect(480, 400, 260, 120)
            ],
            new Rect(24, 612, 900, 54),
            new Rect(24, 688, 900, 120),
            InlineChildren: false,
            RouteCorridorWidth: 90,
            ChildCardWidth: 260,
            ChildCardHeight: 120,
            TitleBlockHeight: 74,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: true,
            [
                new SchematicConnectionRouteRequest("current-0", "clk", "clk", 1, new Point(560, 224), new Point(180, 442), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("current-1", "rst_n", "rst_n", 1, new Point(560, 264), new Point(480, 478), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("local-0", "sum", "sum", 8, new Point(300, 640), new Point(740, 434), SourceFromLocalSignal: true, TargetIsInput: false),
                new SchematicConnectionRouteRequest("local-1", "carry", "carry", 1, new Point(340, 640), new Point(740, 474), SourceFromLocalSignal: true, TargetIsInput: false)
            ]));

        SchematicConnectionRoute[] currentRoutes = routes.Where(static route => route.Id.StartsWith("current-", StringComparison.OrdinalIgnoreCase)).ToArray();
        SchematicConnectionRoute[] localRoutes = routes.Where(static route => route.Id.StartsWith("local-", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.All(currentRoutes, route =>
        {
            double laneY = route.Points[1].Y;
            Assert.True(laneY > layout.CurrentNodeRect.Bottom);
            Assert.True(laneY < layout.ChildNodeRects.Min(static rect => rect.Y));
            Assert.Equal(laneY, route.Points[2].Y);
        });

        Assert.All(localRoutes, route =>
        {
            double laneY = route.Points[1].Y;
            Assert.True(laneY > layout.ChildNodeRects.Max(static rect => rect.Bottom));
            Assert.True(laneY < layout.LocalSectionRect!.Value.Y);
            Assert.Equal(laneY, route.Points[2].Y);
        });

        Assert.Equal(2, currentRoutes.Select(static route => route.Points[1].Y).Distinct().Count());
        Assert.Equal(2, localRoutes.Select(static route => route.Points[1].Y).Distinct().Count());
    }

    [Fact]
    public void RouteLabelsAreClampedAndSeparatedInsideThePanel()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 760, 420),
            new Rect(120, 120, 220, 120),
            null,
            [
                new Rect(450, 120, 220, 80),
                new Rect(450, 210, 220, 80)
            ],
            new Rect(24, 330, 680, 42),
            new Rect(24, 380, 680, 36),
            InlineChildren: true,
            RouteCorridorWidth: 80,
            ChildCardWidth: 220,
            ChildCardHeight: 80,
            TitleBlockHeight: 74,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: true,
            [
                new SchematicConnectionRouteRequest("a", "wide_a", "wide_a", 32, new Point(340, 150), new Point(450, 150), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("b", "wide_b", "wide_b", 32, new Point(340, 153), new Point(450, 154), SourceFromLocalSignal: false, TargetIsInput: true),
                new SchematicConnectionRouteRequest("c", "wide_c", "wide_c", 32, new Point(340, 156), new Point(450, 158), SourceFromLocalSignal: false, TargetIsInput: true)
            ]));

        Assert.All(routes, route =>
        {
            Assert.True(layout.PanelRect.Contains(route.LabelBounds.TopLeft));
            Assert.True(layout.PanelRect.Contains(route.LabelBounds.BottomRight));
        });

        for (int i = 0; i < routes.Count; i++)
        {
            for (int j = i + 1; j < routes.Count; j++)
            {
                Assert.False(routes[i].LabelBounds.Intersects(routes[j].LabelBounds));
            }
        }
    }
}
