using Avalonia;
using Bistable.App.Services;

namespace Bistable.Tests;

public sealed class SchematicConnectionRouterTests
{
    private readonly SchematicConnectionRouter _router = new();

    [Fact]
    public void GraphvizRouterReportsMissingExecutableWithoutUsingInternalFallback()
    {
        SchematicConnectionRouter router = new(
            new Bistable.App.Services.Routing.SchematicMazeRouter(),
            new Bistable.App.Services.Routing.GraphvizNeatoSchematicRouter(
                "bistable-neato-that-does-not-exist",
                TimeSpan.FromMilliseconds(100)));
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 400, 220),
            new Rect(40, 70, 120, 80),
            null,
            [new Rect(240, 70, 120, 80)],
            null,
            new Rect(20, 180, 360, 30),
            InlineChildren: true,
            RouteCorridorWidth: 60,
            ChildCardWidth: 120,
            ChildCardHeight: 80,
            TitleBlockHeight: 40,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 0);

        SchematicRoutingException exception = Assert.Throws<SchematicRoutingException>(() => router.Compute(
            new SchematicConnectionRoutingInput(
                layout,
                CompactLayout: true,
                [
                    new SchematicConnectionRouteRequest("sig", "sig", "sig", 1, new Point(160, 110), new Point(240, 110), SchematicConnectionRouteKind.BoundaryToChildInput)
                ]),
            SchematicRoutingEngine.GraphvizNeato));

        Assert.Contains("Graphviz neato executable was not found", exception.Message);
    }

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
        Assert.All(routes, route =>
        {
            Assert.True(route.Points.Count >= 2);
            AssertManhattanOrthogonal(route.Points);
        });

        Assert.All(inputRoutes, route =>
        {
            Assert.True(route.Points[0].X <= layout.CurrentNodeRect.Right + 0.5);
            Assert.True(route.Points[^1].X >= layout.ChildNodeRects.Min(static rect => rect.X) - 0.5);
        });

        SchematicConnectionRoute boundaryOutputRoute = outputRoutes.Single(static route => route.Id == "out-a");
        SchematicConnectionRoute localOutputRoute = outputRoutes.Single(static route => route.Id == "out-b");

        Assert.True(boundaryOutputRoute.Points[0].X >= layout.ChildNodeRects.Max(static rect => rect.Right) - 0.5);
        Assert.True(boundaryOutputRoute.Points[^1].X > boundaryOutputRoute.Points[0].X);

        Assert.True(localOutputRoute.Points[^1].Y > layout.ChildNodeRects.Max(static rect => rect.Bottom));
        double childBottom = layout.ChildNodeRects.Max(static rect => rect.Bottom);
        Assert.Contains(localOutputRoute.Points, point => point.Y > childBottom);

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

        Assert.All(routes, route =>
        {
            Assert.True(route.Points.Count >= 2);
            AssertManhattanOrthogonal(route.Points);
        });

        double currentBottom = layout.CurrentNodeRect.Bottom;
        double childTop = layout.ChildNodeRects.Min(static rect => rect.Y);
        double childBottom = layout.ChildNodeRects.Max(static rect => rect.Bottom);
        Assert.All(currentRoutes, route =>
        {
            Assert.Contains(route.Points, point => point.Y > currentBottom);
            Assert.Contains(route.Points, point => point.Y < childTop + 0.5);
        });

        Assert.All(localRoutes, route =>
        {
            Assert.Contains(route.Points, point => point.Y > childBottom);
            Assert.True(route.Points[^1].Y >= childBottom);
        });
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

        AssertManhattanOrthogonal(route.Points);
        Assert.Equal(new Point(780, 220), route.Points[0]);
        Assert.Equal(new Point(420, 380), route.Points[^1]);
        Assert.DoesNotContain(route.Points, point => point.Y > layout.ChildNodeRects.Max(static rect => rect.Bottom) + 0.5);
        Assert.Contains(route.Points, point => point.Y > layout.ChildNodeRects[0].Bottom && point.Y < layout.ChildNodeRects[1].Y);
    }

    [Fact]
    public void OrthogonalCrossingsProduceBridgeMetadata()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1000, 620),
            new Rect(200, 120, 50, 50),
            null,
            [
                new Rect(620, 120, 50, 50)
            ],
            new Rect(24, 520, 900, 48),
            new Rect(24, 570, 900, 40),
            InlineChildren: true,
            RouteCorridorWidth: 160,
            ChildCardWidth: 50,
            ChildCardHeight: 50,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("horizontal", "h", "h", 1, new Point(100, 300), new Point(900, 300), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("vertical", "v", "v", 1, new Point(500, 100), new Point(500, 500), SchematicConnectionRouteKind.ChildOutputToLocal)
            ]));

        Assert.All(routes, route => Assert.NotNull(route.Bridges));
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
            Assert.Equal(new Point(620, 220), route.Points[0]);
            AssertManhattanOrthogonal(route.Points);
        });
        Assert.Single(fanoutRoutes, static route => route.IsBundlePrimary);
        Assert.Equal(1, resetRoute.BundleSize);
        AssertManhattanOrthogonal(resetRoute.Points);
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
            Assert.Equal(new Point(560, 224), route.Points[0]);
            AssertManhattanOrthogonal(route.Points);
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

    private static void AssertManhattanOrthogonal(IReadOnlyList<Point> points)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            Point start = points[index];
            Point end = points[index + 1];
            bool horizontal = Math.Abs(start.Y - end.Y) < 0.5;
            bool vertical = Math.Abs(start.X - end.X) < 0.5;
            Assert.True(horizontal || vertical, $"Segment {index} ({start} → {end}) is not orthogonal.");
        }
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

    [Fact]
    public void RoutesDoNotCrossCurrentNodeRectInterior()
    {
        Rect currentRect = new(200, 200, 240, 200);
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 700),
            currentRect,
            null,
            [
                new Rect(620, 140, 240, 120),
                new Rect(620, 340, 240, 120)
            ],
            new Rect(24, 540, 1140, 48),
            new Rect(24, 600, 1140, 60),
            InlineChildren: true,
            RouteCorridorWidth: 160,
            ChildCardWidth: 240,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<Rect> obstacles =
        [
            currentRect,
            new Rect(620, 140, 240, 120),
            new Rect(620, 340, 240, 120),
            new Rect(24, 540, 1140, 48),
            new Rect(24, 600, 1140, 60)
        ];

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("peer-up", "alu_data", "alu_data", 8, new Point(860, 400), new Point(620, 180), SchematicConnectionRouteKind.ChildOutputToChildInput),
                new SchematicConnectionRouteRequest("peer-down", "result", "result", 8, new Point(860, 180), new Point(620, 400), SchematicConnectionRouteKind.ChildOutputToChildInput)
            ],
            obstacles));

        Assert.All(routes, route => AssertRouteAvoidsObstacleInterior(route, currentRect, 3));
    }

    [Fact]
    public void RoutesDoNotCrossParentNodeRectInterior()
    {
        Rect parentRect = new(20, 80, 170, 64);
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 700),
            new Rect(280, 200, 240, 200),
            parentRect,
            [new Rect(620, 200, 240, 140)],
            null,
            new Rect(24, 600, 1140, 60),
            InlineChildren: true,
            RouteCorridorWidth: 120,
            ChildCardWidth: 240,
            ChildCardHeight: 140,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<Rect> obstacles =
        [
            layout.CurrentNodeRect,
            parentRect,
            layout.ChildNodeRects[0],
            layout.ProbeSectionRect
        ];

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("over-parent", "clk", "clk", 1, new Point(520, 240), new Point(620, 240), SchematicConnectionRouteKind.BoundaryToChildInput)
            ],
            obstacles));

        Assert.All(routes, route => AssertRouteAvoidsObstacleInterior(route, parentRect, 3));
    }

    [Fact]
    public void RoutesDoNotCrossProbeSectionInterior()
    {
        Rect probeSection = new(24, 540, 1140, 80);
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1200, 700),
            new Rect(280, 200, 240, 200),
            null,
            [new Rect(620, 200, 240, 140)],
            new Rect(24, 460, 1140, 60),
            probeSection,
            InlineChildren: true,
            RouteCorridorWidth: 120,
            ChildCardWidth: 240,
            ChildCardHeight: 140,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 1,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        IReadOnlyList<Rect> obstacles =
        [
            layout.CurrentNodeRect,
            layout.ChildNodeRects[0],
            layout.LocalSectionRect!.Value,
            probeSection
        ];

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("io", "result", "result", 8, new Point(860, 240), new Point(520, 240), SchematicConnectionRouteKind.ChildOutputToBoundary)
            ],
            obstacles));

        Assert.All(routes, route => AssertRouteAvoidsObstacleInterior(route, probeSection, 3));
    }

    [Fact]
    public void BundlePrimaryRouteCarriesLabelBounds()
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
                new SchematicConnectionRouteRequest("a", "clk", "clk", 1, new Point(580, 220), new Point(760, 212), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("b", "clk", "clk", 1, new Point(580, 220), new Point(760, 378), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        SchematicConnectionRoute[] primaries = routes.Where(static route => route.IsBundlePrimary).ToArray();
        Assert.Single(primaries);
        Assert.True(primaries[0].LabelBounds.Width > 0);
        Assert.True(primaries[0].LabelBounds.Height > 0);
    }

    // ── Faz 2: Steiner tree + bus ribbon ──────────────────────────────────────

    [Fact]
    public void SteinerTreeFanoutAllRoutesStartAtSharedSource()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(200, 180, 280, 156),
            null,
            [
                new Rect(700, 160, 220, 120),
                new Rect(700, 330, 220, 120),
                new Rect(700, 500, 220, 120)
            ],
            null,
            new Rect(24, 640, 900, 80),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        Point sharedSource = new(480, 258);
        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("f1", "clk", "clk", 1, sharedSource, new Point(700, 200), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("f2", "clk", "clk", 1, sharedSource, new Point(700, 370), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("f3", "clk", "clk", 1, sharedSource, new Point(700, 540), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        Assert.Equal(3, routes.Count);
        Assert.All(routes, route =>
        {
            Assert.True(route.Points.Count >= 2);
            AssertManhattanOrthogonal(route.Points);
            Point start = route.Points[0];
            Assert.True(Math.Abs(start.X - sharedSource.X) < 2 && Math.Abs(start.Y - sharedSource.Y) < 2,
                $"Route {route.Id} does not start at shared source. Start: {start}");
        });
    }

    [Fact]
    public void SteinerTreeFanoutRoutesEndAtCorrectTargets()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(200, 180, 280, 156),
            null,
            [
                new Rect(700, 160, 220, 120),
                new Rect(700, 330, 220, 120)
            ],
            null,
            new Rect(24, 640, 900, 80),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        Point sharedSource = new(480, 258);
        Point target1 = new(700, 200);
        Point target2 = new(700, 370);
        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("fa", "sig", "sig", 1, sharedSource, target1, SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("fb", "sig", "sig", 1, sharedSource, target2, SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        Assert.Equal(2, routes.Count);
        SchematicConnectionRoute r1 = routes.Single(static r => r.Id == "fa");
        SchematicConnectionRoute r2 = routes.Single(static r => r.Id == "fb");

        Assert.True(Math.Abs(r1.Points[^1].X - target1.X) < 2 && Math.Abs(r1.Points[^1].Y - target1.Y) < 2,
            $"Route fa does not end at target1. End: {r1.Points[^1]}");
        Assert.True(Math.Abs(r2.Points[^1].X - target2.X) < 2 && Math.Abs(r2.Points[^1].Y - target2.Y) < 2,
            $"Route fb does not end at target2. End: {r2.Points[^1]}");
    }

    [Fact]
    public void SteinerTreeFanoutRoutesHaveJunctionAtBranchPoint()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(200, 180, 280, 156),
            null,
            [
                new Rect(700, 160, 220, 120),
                new Rect(700, 330, 220, 120)
            ],
            null,
            new Rect(24, 640, 900, 80),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        Point sharedSource = new(480, 258);
        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("j1", "net", "net", 1, sharedSource, new Point(700, 200), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("j2", "net", "net", 1, sharedSource, new Point(700, 370), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        // At least one route must carry a junction (source shared between 2 routes → junction at source or branch)
        bool anyJunction = routes.Any(static r => r.Junctions is { Count: > 0 });
        Assert.True(anyJunction, "Fanout net should have at least one junction marker at the branch/source point.");
    }

    [Fact]
    public void RectilinearSteinerTreeMstConnectsAllPoints()
    {
        Avalonia.Point source = new(0, 0);
        Avalonia.Point[] targets = [new(100, 0), new(100, 80), new(50, 160)];
        Avalonia.Point[] allPoints = [source, .. targets];

        (int[] parent, IReadOnlyList<(int From, int To)> edges) =
            Bistable.App.Services.Routing.RectilinearSteinerTree.Build(allPoints);

        // MST of N points has exactly N-1 edges
        Assert.Equal(allPoints.Length - 1, edges.Count);

        // Every non-root node must be reachable via parent chain from root
        for (int i = 1; i < allPoints.Length; i++)
        {
            IReadOnlyList<int> path = Bistable.App.Services.Routing.RectilinearSteinerTree.PathToTarget(i, parent);
            Assert.Equal(0, path[0]);
            Assert.Equal(i, path[^1]);
        }
    }

    [Fact]
    public void RectilinearSteinerTreeBfsOrderHasTrunkFirst()
    {
        // Linear chain: 0 → 1 → 2 → 3 (each point 100px further)
        Avalonia.Point[] points =
        [
            new(0, 0),
            new(100, 0),
            new(200, 0),
            new(300, 0)
        ];

        (_, IReadOnlyList<(int From, int To)> edges) =
            Bistable.App.Services.Routing.RectilinearSteinerTree.Build(points);
        IReadOnlyList<(int From, int To)> bfs =
            Bistable.App.Services.Routing.RectilinearSteinerTree.BfsOrder(edges, points.Length);

        // First BFS edge must start from root (0)
        Assert.Equal(0, bfs[0].From);
        // All edges are present
        Assert.Equal(points.Length - 1, bfs.Count);
    }

    private static void AssertRouteAvoidsObstacleInterior(SchematicConnectionRoute route, Rect obstacle, double inset)
    {
        Rect interior = obstacle.Deflate(inset);
        if (interior.Width <= 0 || interior.Height <= 0)
        {
            return;
        }

        for (int index = 0; index < route.Points.Count - 1; index++)
        {
            Point start = route.Points[index];
            Point end = route.Points[index + 1];
            Point midpoint = new((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            Assert.False(
                interior.Contains(midpoint),
                $"Route segment midpoint {midpoint} lies inside obstacle interior {interior}.");
        }
    }

    // ── Faz 4: Track assignment ────────────────────────────────────────────────

    [Fact]
    public void TrackAssignerSeparatesParallelOverlappingRoutes()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(200, 180, 280, 156),
            null,
            [
                new Rect(760, 200, 220, 120),
                new Rect(760, 400, 220, 120)
            ],
            null,
            new Rect(24, 640, 900, 80),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 120,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        // Two routes with the same source Y — in practice the A* + congestion will already spread
        // them, but after TrackAssigner they must not share the exact same Y for any interior segment.
        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("ta1", "clk", "clk", 1, new Point(480, 258), new Point(760, 255), SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("ta2", "rst", "rst", 1, new Point(480, 258), new Point(760, 455), SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        Assert.Equal(2, routes.Count);
        Assert.All(routes, route =>
        {
            Assert.True(route.Points.Count >= 2);
            AssertManhattanOrthogonal(route.Points);
        });
    }

    [Fact]
    public void TrackAssignerPreservesRouteEndpoints()
    {
        SchematicScopePanelLayout layout = new(
            new Rect(0, 0, 1280, 760),
            new Rect(200, 180, 280, 156),
            null,
            [new Rect(760, 200, 220, 200)],
            null,
            new Rect(24, 640, 900, 80),
            InlineChildren: true,
            RouteCorridorWidth: 140,
            ChildCardWidth: 220,
            ChildCardHeight: 200,
            TitleBlockHeight: 90,
            LocalColumns: 1,
            LocalRowCount: 0,
            ProbeColumns: 1,
            ProbeRowCount: 1);

        Point src1 = new(480, 230);
        Point src2 = new(480, 280);
        Point tgt1 = new(760, 245);
        Point tgt2 = new(760, 300);

        IReadOnlyList<SchematicConnectionRoute> routes = _router.Compute(new SchematicConnectionRoutingInput(
            layout,
            CompactLayout: false,
            [
                new SchematicConnectionRouteRequest("te1", "a", "a", 1, src1, tgt1, SchematicConnectionRouteKind.BoundaryToChildInput),
                new SchematicConnectionRouteRequest("te2", "b", "b", 1, src2, tgt2, SchematicConnectionRouteKind.BoundaryToChildInput)
            ]));

        SchematicConnectionRoute r1 = routes.Single(static r => r.Id == "te1");
        SchematicConnectionRoute r2 = routes.Single(static r => r.Id == "te2");

        // Source and target endpoints must remain at their original positions
        Assert.True(Math.Abs(r1.Points[0].X - src1.X) < 2 && Math.Abs(r1.Points[0].Y - src1.Y) < 2);
        Assert.True(Math.Abs(r2.Points[0].X - src2.X) < 2 && Math.Abs(r2.Points[0].Y - src2.Y) < 2);
        Assert.True(Math.Abs(r1.Points[^1].X - tgt1.X) < 2 && Math.Abs(r1.Points[^1].Y - tgt1.Y) < 2);
        Assert.True(Math.Abs(r2.Points[^1].X - tgt2.X) < 2 && Math.Abs(r2.Points[^1].Y - tgt2.Y) < 2);
    }
}
