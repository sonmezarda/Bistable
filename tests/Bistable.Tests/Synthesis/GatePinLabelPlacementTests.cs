using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;

namespace Bistable.Tests.Synthesis;

public sealed class GatePinLabelPlacementTests
{
    private static readonly Rect Viewport = new(-200, -200, 800, 800);

    [Fact]
    public void Place_NearbyLabels_DoNotOverlap()
    {
        GatePinLabelPlacementRequest[] requests =
        [
            Request("a", "node", new Point(100, 100)),
            Request("b", "node", new Point(100, 104)),
        ];

        IReadOnlyDictionary<string, GatePinLabelPlacement> placements =
            GatePinLabelPlacementEngine.Place(requests, [], Viewport, zoom: 1);

        Assert.Equal(2, placements.Count);
        Assert.False(placements["a"].TextBounds.Intersects(placements["b"].TextBounds));
    }

    [Fact]
    public void Place_TitleObstacle_UsesOutsideCandidate()
    {
        GatePinLabelPlacementRequest request =
            Request("pin", "module", new Point(100, 24), textWidth: 60);
        GatePinLabelObstacle title = new(new Rect(100, 0, 140, 40));

        GatePinLabelPlacement placement = Assert.Single(
            GatePinLabelPlacementEngine.Place(
                [request],
                [title],
                Viewport,
                zoom: 1)).Value;

        Assert.Contains(
            placement.Kind,
            new[]
            {
                GatePinLabelPlacementKind.OutsideAbove,
                GatePinLabelPlacementKind.OutsideBelow,
            });
        Assert.False(placement.TextBounds.Intersects(title.Bounds));
    }

    [Fact]
    public void Place_ModuleBodyOwnedByRequest_AllowsInsidePlacement()
    {
        GatePinLabelPlacementRequest request =
            Request("pin", "module", new Point(100, 100));
        GatePinLabelObstacle body = new(
            new Rect(90, 60, 180, 120),
            AllowedOwnerNodeId: "module");

        GatePinLabelPlacement placement = Assert.Single(
            GatePinLabelPlacementEngine.Place(
                [request],
                [body],
                Viewport,
                zoom: 1)).Value;

        Assert.Equal(GatePinLabelPlacementKind.InsideAbove, placement.Kind);
    }

    [Fact]
    public void Place_PrimitiveBody_ForcesLabelOutside()
    {
        GatePinLabelPlacementRequest request =
            Request("pin", "gate", new Point(100, 100));
        GatePinLabelObstacle body = new(new Rect(100, 60, 180, 120));

        GatePinLabelPlacement placement = Assert.Single(
            GatePinLabelPlacementEngine.Place(
                [request],
                [body],
                Viewport,
                zoom: 1)).Value;

        Assert.Contains(
            placement.Kind,
            new[]
            {
                GatePinLabelPlacementKind.OutsideAbove,
                GatePinLabelPlacementKind.OutsideBelow,
            });
    }

    [Fact]
    public void Place_OwnPortObstacle_AllowsLabelCandidate()
    {
        GatePinLabelPlacementRequest request =
            Request("pin", "module", new Point(100, 100));
        GatePinLabelObstacle ownPort = new(
            new Rect(97, 97, 6, 6),
            AllowedRequestId: "pin");

        GatePinLabelPlacement placement = Assert.Single(
            GatePinLabelPlacementEngine.Place(
                [request],
                [ownPort],
                Viewport,
                zoom: 1)).Value;

        Assert.Equal(GatePinLabelPlacementKind.InsideAbove, placement.Kind);
    }

    [Fact]
    public void Place_NeighbourPortObstacle_RejectsOverlappingCandidate()
    {
        GatePinLabelPlacementRequest request =
            Request("pin", "module", new Point(100, 100));
        GatePinLabelObstacle neighbourPort = new(new Rect(105, 84, 6, 6));

        GatePinLabelPlacement placement = Assert.Single(
            GatePinLabelPlacementEngine.Place(
                [request],
                [neighbourPort],
                Viewport,
                zoom: 1)).Value;

        Assert.NotEqual(GatePinLabelPlacementKind.InsideAbove, placement.Kind);
        Assert.False(placement.TextBounds.Intersects(neighbourPort.Bounds));
    }

    [Fact]
    public void Place_PriorityRequest_WinsOnlyAvailableSlot()
    {
        GatePinLabelPlacementRequest normal =
            Request("normal", "z-node", new Point(100, 100));
        GatePinLabelPlacementRequest priority =
            Request("priority", "a-node", new Point(100, 100), priority: true);
        GatePinLabelObstacle[] blockers =
        [
            new(new Rect(104, 103, 70, 20)),
            new(new Rect(24, 86, 70, 20)),
            new(new Rect(24, 103, 70, 20)),
        ];

        IReadOnlyDictionary<string, GatePinLabelPlacement> placements =
            GatePinLabelPlacementEngine.Place(
                [normal, priority],
                blockers,
                Viewport,
                zoom: 1);

        Assert.Contains("priority", placements.Keys);
        Assert.DoesNotContain("normal", placements.Keys);
    }

    [Fact]
    public void Place_IsDeterministicAcrossRepeatedRuns()
    {
        GatePinLabelPlacementRequest[] requests =
        [
            Request("c", "node-b", new Point(100, 108)),
            Request("a", "node-a", new Point(100, 100)),
            Request("b", "node-a", new Point(100, 104)),
        ];

        IReadOnlyDictionary<string, GatePinLabelPlacement> first =
            GatePinLabelPlacementEngine.Place(requests, [], Viewport, zoom: 0.75);
        IReadOnlyDictionary<string, GatePinLabelPlacement> second =
            GatePinLabelPlacementEngine.Place(requests, [], Viewport, zoom: 0.75);

        Assert.Equal(
            first.OrderBy(static pair => pair.Key),
            second.OrderBy(static pair => pair.Key));
    }

    [Fact]
    public void Place_OutsideViewport_IsNotMaterialized()
    {
        GatePinLabelPlacementRequest request =
            Request("offscreen", "node", new Point(2_000, 2_000));

        IReadOnlyDictionary<string, GatePinLabelPlacement> placements =
            GatePinLabelPlacementEngine.Place(
                [request],
                [],
                new Rect(0, 0, 800, 600),
                zoom: 1);

        Assert.Empty(placements);
    }

    [Fact]
    public void NodeSpatialIndex_QueryReturnsOnlyVisibleNodesWithNestedOffsets()
    {
        ElkNode visibleChild = new()
        {
            Id = "visible-child",
            X = 20,
            Y = 30,
            Width = 40,
            Height = 40,
        };
        ElkNode root = new()
        {
            Id = "root",
            X = 100,
            Y = 200,
            Width = 300,
            Height = 300,
            Children = [visibleChild],
        };
        ElkNode offscreen = new()
        {
            Id = "offscreen",
            X = 5_000,
            Y = 5_000,
            Width = 40,
            Height = 40,
        };
        GateNodeSpatialIndex index = GateNodeSpatialIndex.Build([root, offscreen]);

        IReadOnlyList<GateVisibleNode> visible =
            index.Query(new Rect(110, 220, 100, 100));

        Assert.Equal(["root", "visible-child"], visible.Select(static node => node.Node.Id));
        GateVisibleNode child = visible.Single(node => node.Node.Id == "visible-child");
        Assert.Equal(120, child.AbsoluteX);
        Assert.Equal(230, child.AbsoluteY);
        Assert.DoesNotContain(visible, node => node.Node.Id == "offscreen");
    }

    [Fact]
    public void NodeSpatialIndex_LargeNode_IsQueryableWithoutCellExplosion()
    {
        ElkNode large = new()
        {
            Id = "large",
            X = 100,
            Y = 100,
            Width = 10_000,
            Height = 10_000,
        };
        GateNodeSpatialIndex index = GateNodeSpatialIndex.Build([large]);

        GateVisibleNode visible = Assert.Single(
            index.Query(new Rect(9_000, 9_000, 100, 100)));

        Assert.Equal("large", visible.Node.Id);
    }

    [Fact]
    public void NodeSpatialIndex_QueryReturnsEachNodeOnceInStableOrder()
    {
        ElkNode first = new()
        {
            Id = "first",
            X = 0,
            Y = 0,
            Width = 600,
            Height = 600,
        };
        ElkNode second = new()
        {
            Id = "second",
            X = 100,
            Y = 100,
            Width = 50,
            Height = 50,
        };
        GateNodeSpatialIndex index = GateNodeSpatialIndex.Build([first, second]);

        IReadOnlyList<GateVisibleNode> visible =
            index.Query(new Rect(0, 0, 600, 600));

        Assert.Equal(["first", "second"], visible.Select(static node => node.Node.Id));
    }

    private static GatePinLabelPlacementRequest Request(
        string id,
        string nodeId,
        Point centre,
        double textWidth = 60,
        bool priority = false) =>
        new(
            id,
            nodeId,
            centre,
            IsWestSide: true,
            new Size(textWidth, 10),
            priority);
}
