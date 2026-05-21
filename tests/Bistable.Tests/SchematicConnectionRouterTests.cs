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
                new SchematicConnectionRouteRequest("in-a", "clk", "clk", 1, new Point(580, 220), new Point(760, 212), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("in-b", "rst_n", "rst_n", 1, new Point(580, 262), new Point(760, 378), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("out-a", "sum", "sum", 8, new Point(980, 244), new Point(1180, 240), SchematicConnectionRouteKind.ChildOutputToBoundary),
                new SchematicConnectionRouteRequest("out-b", "carry", "carry", 1, new Point(980, 398), new Point(260, 552), SchematicConnectionRouteKind.ChildOutputToLocal)
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

        SchematicConnectionRoute[] boundaryOutputRoutes = outputRoutes
            .Where(static route => route.Id == "out-a")
            .ToArray();
        SchematicConnectionRoute[] localOutputRoutes = outputRoutes
            .Where(static route => route.Id == "out-b")
            .ToArray();

        Assert.All(boundaryOutputRoutes, route =>
        {
            double laneX = route.Points[^2].X;
            Assert.True(laneX > layout.ChildNodeRects.Max(static rect => rect.Right));
            Assert.Equal(laneX, route.Points[^3].X);
        });

        Assert.All(localOutputRoutes, route =>
        {
            double laneY = route.Points[2].Y;
            Assert.True(laneY > layout.ChildNodeRects.Max(static rect => rect.Bottom));
            Assert.Equal(laneY, route.Points[3].Y);
            Assert.True(route.Points[1].X > route.Points[0].X);
            Assert.True(route.Points[^2].X > route.Points[^1].X);
        });

        Assert.Equal(2, inputRoutes.Select(static route => route.Points[1].X).Distinct().Count());
        Assert.Single(boundaryOutputRoutes.Select(static route => route.Points[^2].X).Distinct());
        Assert.All(routes, route => Assert.True(route.LabelBounds.Width > 0));
    }

    [Fact]
    public void ExpandedBoundaryOutputRoutesFromChildTowardBoundaryWithoutBacktracking()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1120, 420),
            new Rect(0, 64, 1120, 260),
            null,
            [new Rect(380, 120, 260, 120)],
            null,
            new Rect(24, 360, 900, 40),
            InlineChildren: true,
            RouteCorridorWidth: 120,
            ChildCardWidth: 260,
            ChildCardHeight: 120,
            TitleBlockHeight: 0,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 0);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest(
                    "u_core:result",
                    "result",
                    "result",
                    8,
                    new Point(640, 178),
                    new Point(1030, 174),
                    SchematicConnectionRouteKind.ChildOutputToBoundary)
            ],
            [layout.ChildNodeRects[0]]));

        SchematicConnectionRoute route = Assert.Single(routes);
        double minimumX = route.Points.Min(static point => point.X);

        Assert.Equal(new Point(640, 178), route.Points[0]);
        Assert.Equal(new Point(1030, 174), route.Points[^1]);
        Assert.True(minimumX >= 640);
        Assert.All(route.Points.Skip(1), point => Assert.True(point.X >= 640));
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
                new SchematicConnectionRouteRequest("current-0", "clk", "clk", 1, new Point(560, 224), new Point(180, 442), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("current-1", "rst_n", "rst_n", 1, new Point(560, 264), new Point(480, 478), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("local-0", "sum", "sum", 8, new Point(740, 434), new Point(300, 640), SchematicConnectionRouteKind.ChildOutputToLocal),
                new SchematicConnectionRouteRequest("local-1", "carry", "carry", 1, new Point(740, 474), new Point(340, 640), SchematicConnectionRouteKind.ChildOutputToLocal)
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
            double laneY = route.Points[2].Y;
            Assert.True(laneY > layout.ChildNodeRects.Max(static rect => rect.Bottom));
            Assert.True(laneY < layout.LocalSectionRect!.Value.Y);
            Assert.Equal(laneY, route.Points[3].Y);
        });

        Assert.Equal(2, currentRoutes.Select(static route => route.Points[1].Y).Distinct().Count());
        Assert.Equal(2, localRoutes.Select(static route => route.Points[2].Y).Distinct().Count());
    }

    [Fact]
    public void DifferentNetsDoNotShareCollinearSegments()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(240, 160, 300, 180),
            null,
            [
                new Rect(800, 160, 260, 140),
                new Rect(800, 340, 260, 140)
            ],
            new Rect(24, 570, 900, 48),
            new Rect(24, 630, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 240,
            ChildCardWidth: 260,
            ChildCardHeight: 140,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("a", "a", "a", 8, new Point(540, 220), new Point(800, 220), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("b", "b", "b", 8, new Point(540, 224), new Point(800, 224), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("c", "c", "c", 8, new Point(540, 228), new Point(800, 228), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        for (int first = 0; first < routes.Count; first++)
        {
            for (int second = first + 1; second < routes.Count; second++)
            {
                AssertNoCollinearOverlap(routes[first], routes[second]);
            }
        }
    }

    [Fact]
    public void FanoutNetProducesJunctionMetadata()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1400, 760),
            new Rect(300, 180, 320, 156),
            null,
            [
                new Rect(860, 180, 260, 120),
                new Rect(860, 336, 260, 120)
            ],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 220,
            ChildCardWidth: 260,
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
                new SchematicConnectionRouteRequest("clk-a", "clk", "clk", 1, new Point(620, 220), new Point(860, 210), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("clk-b", "clk", "clk", 1, new Point(620, 220), new Point(860, 366), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        Assert.All(routes, route => Assert.NotEmpty(route.Junctions ?? []));
    }

    [Fact]
    public void ChildOutputToChildInputRoutesThroughSideCorridor()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 760),
            new Rect(0, 80, 1200, 520),
            null,
            [
                new Rect(420, 150, 360, 120),
                new Rect(420, 330, 360, 120)
            ],
            new Rect(24, 620, 900, 48),
            new Rect(24, 680, 900, 60),
            InlineChildren: true,
            RouteCorridorWidth: 180,
            ChildCardWidth: 360,
            ChildCardHeight: 120,
            TitleBlockHeight: 0,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest(
                    "parity",
                    "parity_i",
                    "system_top.u_core.parity_i",
                    1,
                    new Point(780, 220),
                    new Point(420, 380),
                    SchematicConnectionRouteKind.ChildOutputToChildInput)
            ],
            layout.ChildNodeRects));

        SchematicConnectionRoute route = Assert.Single(routes);

        Assert.True(route.Points[1].X > route.Points[0].X);
        Assert.True(route.Points[2].X > layout.ChildNodeRects.Max(static rect => rect.Right));
        Assert.DoesNotContain(route.Points, point => point.Y > layout.ChildNodeRects.Max(static rect => rect.Bottom));
        Assert.Contains(route.Points, point => point.Y > layout.ChildNodeRects[0].Bottom && point.Y < layout.ChildNodeRects[1].Y);
    }

    [Fact]
    public void OrthogonalCrossingsProduceBridgeMetadata()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1000, 620),
            new Rect(200, 120, 240, 140),
            null,
            [
                new Rect(620, 120, 240, 140)
            ],
            new Rect(24, 520, 900, 48),
            new Rect(24, 570, 900, 40),
            InlineChildren: true,
            RouteCorridorWidth: 160,
            ChildCardWidth: 240,
            ChildCardHeight: 140,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("in", "input_bus", "input_bus", 8, new Point(440, 210), new Point(620, 500), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("out", "result", "result", 8, new Point(860, 180), new Point(460, 500), SchematicConnectionRouteKind.ChildOutputToLocal)
            ]));

        Assert.Contains(routes, static route => route.Bridges is { Count: > 0 });
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
                new SchematicConnectionRouteRequest("a", "wide_a", "wide_a", 32, new Point(340, 150), new Point(450, 150), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("b", "wide_b", "wide_b", 32, new Point(340, 153), new Point(450, 154), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("c", "wide_c", "wide_c", 32, new Point(340, 156), new Point(450, 158), SchematicConnectionRouteKind.BoundaryToChildInput)
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

    [Fact]
    public void InlineFanoutRoutesShareOneBundleLane()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1400, 760),
            new Rect(300, 180, 320, 156),
            null,
            [
                new Rect(860, 180, 260, 120),
                new Rect(860, 336, 260, 120)
            ],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 220,
            ChildCardWidth: 260,
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
                new SchematicConnectionRouteRequest("fanout-a", "clk", "clk", 1, new Point(620, 220), new Point(860, 210), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("fanout-b", "clk", "clk", 1, new Point(620, 220), new Point(860, 366), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("rst", "rst_n", "rst_n", 1, new Point(620, 260), new Point(860, 400), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        SchematicConnectionRoute[] fanoutRoutes = routes
            .Where(static route => route.BundleKey == "clk")
            .ToArray();
        SchematicConnectionRoute resetRoute = Assert.Single(routes, static route => route.BundleKey == "rst_n");

        Assert.Equal(2, fanoutRoutes.Length);
        Assert.All(fanoutRoutes, route =>
        {
            Assert.Equal(2, route.BundleSize);
            Assert.Equal(fanoutRoutes[0].Points[1].X, route.Points[1].X);
        });
        Assert.Single(fanoutRoutes, static route => route.IsBundlePrimary);
        Assert.NotEqual(resetRoute.Points[1].X, fanoutRoutes[0].Points[1].X);
    }

    [Fact]
    public void StackedFanoutRoutesShareOneTrunkLane()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 980, 940),
            new Rect(320, 180, 240, 140),
            null,
            [
                new Rect(180, 420, 260, 120),
                new Rect(480, 420, 260, 120)
            ],
            new Rect(24, 650, 900, 54),
            new Rect(24, 730, 900, 120),
            InlineChildren: false,
            RouteCorridorWidth: 120,
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
                new SchematicConnectionRouteRequest("valid-a", "valid", "valid", 1, new Point(560, 224), new Point(180, 442), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("valid-b", "valid", "valid", 1, new Point(560, 224), new Point(480, 478), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("sum", "sum", "sum", 8, new Point(740, 474), new Point(300, 670), SchematicConnectionRouteKind.ChildOutputToLocal)
            ]));

        SchematicConnectionRoute[] fanoutRoutes = routes
            .Where(static route => route.BundleKey == "valid")
            .ToArray();

        Assert.Equal(2, fanoutRoutes.Length);
        Assert.All(fanoutRoutes, route =>
        {
            Assert.Equal(2, route.BundleSize);
            Assert.Equal(fanoutRoutes[0].Points[1].Y, route.Points[1].Y);
        });
        Assert.Single(fanoutRoutes, static route => route.IsBundlePrimary);
    }

    [Fact]
    public void InlineRoutesDetourAroundBlockingObstacles()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 760),
            new Rect(300, 160, 200, 120),
            null,
            [new Rect(800, 160, 220, 120)],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 300,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);
        Rect obstacle = new(700, 186, 46, 42);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("data", "data", "data", 8, new Point(500, 207), new Point(800, 207), SchematicConnectionRouteKind.BoundaryToChildInput)
            ],
            [obstacle]));

        SchematicConnectionRoute route = Assert.Single(routes);

        Assert.True(route.Points.Count > 4);
        AssertNoSegmentIntersects(route.Points, obstacle.Inflate(10));
    }

    [Fact]
    public void StackedRoutesDetourAroundBlockingObstacles()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 980, 940),
            new Rect(320, 120, 240, 140),
            null,
            [new Rect(180, 420, 260, 120)],
            new Rect(24, 650, 900, 54),
            new Rect(24, 730, 900, 120),
            InlineChildren: false,
            RouteCorridorWidth: 120,
            ChildCardWidth: 260,
            ChildCardHeight: 120,
            TitleBlockHeight: 74,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);
        Rect obstacle = new(432, 292, 56, 28);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: true,
            [
                new SchematicConnectionRouteRequest("clk", "clk", "clk", 1, new Point(440, 260), new Point(440, 420), SchematicConnectionRouteKind.BoundaryToChildInput)
            ],
            [obstacle]));

        SchematicConnectionRoute route = Assert.Single(routes);

        Assert.True(route.Points.Count > 4);
        AssertNoSegmentIntersects(route.Points, obstacle.Inflate(8));
    }

    [Fact]
    public void RoutesCanDetourAroundMultipleBlockingObstacles()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1300, 760),
            new Rect(300, 160, 200, 120),
            null,
            [new Rect(880, 160, 220, 120)],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 360,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);
        Rect firstObstacle = new(710, 186, 42, 42);
        Rect secondObstacle = new(780, 186, 42, 42);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("data", "data", "data", 8, new Point(500, 207), new Point(880, 207), SchematicConnectionRouteKind.BoundaryToChildInput)
            ],
            [firstObstacle, secondObstacle]));

        SchematicConnectionRoute route = Assert.Single(routes);

        Assert.True(route.Points.Count > 4);
        AssertNoSegmentIntersects(route.Points, firstObstacle.Inflate(10));
        AssertNoSegmentIntersects(route.Points, secondObstacle.Inflate(10));
    }

    [Fact]
    public void RouteLabelsAvoidObstacles()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 760),
            new Rect(300, 160, 200, 120),
            null,
            [new Rect(800, 160, 220, 120)],
            new Rect(24, 540, 900, 48),
            new Rect(24, 604, 900, 96),
            InlineChildren: true,
            RouteCorridorWidth: 300,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 2,
            ProbeRowCount: 2);
        Rect labelObstacle = new(610, 192, 86, 32);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("data", "data", "data", 16, new Point(500, 207), new Point(800, 207), SchematicConnectionRouteKind.BoundaryToChildInput)
            ],
            [labelObstacle]));

        SchematicConnectionRoute route = Assert.Single(routes);

        Assert.False(route.LabelBounds.Intersects(labelObstacle.Inflate(3)));
        Assert.True(layout.PanelRect.Contains(route.LabelBounds.TopLeft));
        Assert.True(layout.PanelRect.Contains(route.LabelBounds.BottomRight));
    }

    private static void AssertNoSegmentIntersects(IReadOnlyList<Point> points, Rect obstacle)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            Assert.False(SegmentIntersects(points[index], points[index + 1], obstacle), $"Segment {index} intersects {obstacle}.");
        }
    }

    private static bool SegmentIntersects(Point start, Point end, Rect rect)
    {
        if (Math.Abs(start.Y - end.Y) < 0.01)
        {
            double minX = Math.Min(start.X, end.X);
            double maxX = Math.Max(start.X, end.X);
            return start.Y > rect.Y
                && start.Y < rect.Bottom
                && maxX > rect.X
                && minX < rect.Right;
        }

        if (Math.Abs(start.X - end.X) < 0.01)
        {
            double minY = Math.Min(start.Y, end.Y);
            double maxY = Math.Max(start.Y, end.Y);
            return start.X > rect.X
                && start.X < rect.Right
                && maxY > rect.Y
                && minY < rect.Bottom;
        }

        return false;
    }

    private static void AssertNoCollinearOverlap(SchematicConnectionRoute first, SchematicConnectionRoute second)
    {
        foreach ((Point firstStart, Point firstEnd) in EnumerateSegments(first.Points))
        {
            foreach ((Point secondStart, Point secondEnd) in EnumerateSegments(second.Points))
            {
                Assert.False(
                    CollinearSegmentsOverlap(firstStart, firstEnd, secondStart, secondEnd),
                    $"{first.Id} and {second.Id} overlap on distinct nets.");
            }
        }
    }

    private static IEnumerable<(Point start, Point end)> EnumerateSegments(IReadOnlyList<Point> points)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            yield return (points[index], points[index + 1]);
        }
    }

    private static bool CollinearSegmentsOverlap(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd)
    {
        if (Math.Abs(firstStart.Y - firstEnd.Y) < 0.01
            && Math.Abs(secondStart.Y - secondEnd.Y) < 0.01
            && Math.Abs(firstStart.Y - secondStart.Y) < 0.01)
        {
            return RangesOverlap(firstStart.X, firstEnd.X, secondStart.X, secondEnd.X);
        }

        if (Math.Abs(firstStart.X - firstEnd.X) < 0.01
            && Math.Abs(secondStart.X - secondEnd.X) < 0.01
            && Math.Abs(firstStart.X - secondStart.X) < 0.01)
        {
            return RangesOverlap(firstStart.Y, firstEnd.Y, secondStart.Y, secondEnd.Y);
        }

        return false;
    }

    private static bool RangesOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        double firstMin = Math.Min(firstStart, firstEnd);
        double firstMax = Math.Max(firstStart, firstEnd);
        double secondMin = Math.Min(secondStart, secondEnd);
        double secondMax = Math.Max(secondStart, secondEnd);
        return Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin) > 0.1;
    }
}
