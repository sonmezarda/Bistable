using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Synthesis;

namespace Bistable.Tests.Synthesis;

public sealed class GatePinInteractionTests
{
    [Fact]
    public void Build_IndexesDirectionRangeNamedNetAndUnconnectedPins()
    {
        ElkNode cellNode = CellNode(
            "gate_u_alu",
            "u_alu",
            Port("gate_u_alu.A[0]", "A[0]", "WEST", 0, 10),
            Port("gate_u_alu.A[1]", "A[1]", "WEST", 0, 30),
            Port("gate_u_alu.Y", "Y", "EAST", 80, 20));
        ElkGraph graph = new()
        {
            Children = [cellNode],
            Edges =
            [
                Edge("e0", 42, "source", "gate_u_alu.A[0]"),
                Edge("e1", 43, "source", "gate_u_alu.A[1]"),
            ],
        };
        GateCell cell = new(
            "u_alu",
            "$_AND_",
            new Dictionary<string, GateConnection>(),
            new Dictionary<string, GatePortDirection>
            {
                ["A"] = GatePortDirection.Input,
                ["Y"] = GatePortDirection.Output,
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        GateModule module = new(
            "top",
            [],
            [cell],
            [new GateNet("operand", [GateBit.Net(42), GateBit.Net(43)])]);

        GatePinInteractionIndex index = GatePinInteractionIndex.Build(graph, module);

        GatePinInfo input = Assert.IsType<GatePinInfo>(index.Get("gate_u_alu.A[0]"));
        Assert.Equal(GatePortDirection.Input, input.Direction);
        Assert.Equal(2, input.Width);
        Assert.Equal(1, input.Msb);
        Assert.Equal(0, input.Lsb);
        Assert.Equal(42, input.NetId);
        Assert.Equal("operand", input.NetName);
        Assert.Contains("Pin: A[0]", input.FormatTooltip(), StringComparison.Ordinal);
        Assert.Contains("Bit/range: [1:0]", input.FormatTooltip(), StringComparison.Ordinal);
        Assert.Contains("Direction: Input", input.FormatTooltip(), StringComparison.Ordinal);

        GatePinInfo output = Assert.IsType<GatePinInfo>(index.Get("gate_u_alu.Y"));
        Assert.Equal(GatePortDirection.Output, output.Direction);
        Assert.False(output.IsConnected);
        Assert.Contains("Net: (unconnected)", output.FormatTooltip(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UsesNestedAbsoluteCoordinates()
    {
        ElkNode child = CellNode(
            "gate_child",
            "child",
            Port("gate_child.A", "A", "WEST", 5, 7));
        ElkNode parent = new()
        {
            Id = "inst_parent",
            X = 100,
            Y = 200,
            Width = 200,
            Height = 200,
            Children = [child],
        };
        child.X = 20;
        child.Y = 30;
        GatePinInteractionIndex index = GatePinInteractionIndex.Build(
            new ElkGraph { Children = [parent] },
            new GateModule("top", [], [], []));

        GatePinInfo info = Assert.IsType<GatePinInfo>(index.Get("gate_child.A"));

        Assert.Equal(new Point(125, 237), info.Centre);
    }

    [Fact]
    public void GetPortIdsForNet_ReturnsEveryEndpointWithoutDuplicates()
    {
        ElkNode source = CellNode(
            "gate_source",
            "source",
            Port("gate_source.Y", "Y", "EAST", 80, 20));
        ElkNode target = CellNode(
            "gate_target",
            "target",
            Port("gate_target.A", "A", "WEST", 0, 20));
        ElkGraph graph = new()
        {
            Children = [source, target],
            Edges =
            [
                Edge("e0", 17, "gate_source.Y", "gate_target.A"),
                Edge("e1", 17, "gate_source.Y", "gate_target.A"),
            ],
        };

        GatePinInteractionIndex index = GatePinInteractionIndex.Build(
            graph,
            new GateModule("top", [], [], []));

        Assert.Equal(
            ["gate_source.Y", "gate_target.A"],
            index.GetPortIdsForNet(17).Order(StringComparer.Ordinal));
        Assert.Empty(index.GetPortIdsForNet(999));
    }

    [Fact]
    public void Build_BoundaryDirectionUsesModuleSemanticsInsteadOfVisualSide()
    {
        ElkNode input = new()
        {
            Id = "boundary_in",
            Ports = [Port("boundary_in.data", "data", "EAST", 80, 20)],
        };
        ElkNode output = new()
        {
            Id = "boundary_out",
            Ports = [Port("boundary_out.result", "result", "WEST", 0, 20)],
        };

        GatePinInteractionIndex index = GatePinInteractionIndex.Build(
            new ElkGraph { Children = [input, output] },
            new GateModule("top", [], [], []));

        Assert.Equal(
            GatePortDirection.Input,
            Assert.IsType<GatePinInfo>(index.Get("boundary_in.data")).Direction);
        Assert.Equal(
            GatePortDirection.Output,
            Assert.IsType<GatePinInfo>(index.Get("boundary_out.result")).Direction);
    }

    private static ElkNode CellNode(
        string id,
        string cellName,
        params ElkPort[] ports) =>
        new()
        {
            Id = id,
            Width = 80,
            Height = 60,
            Labels =
            [
                new ElkLabel { Text = "$_AND_" },
                new ElkLabel { Text = "$_AND_" },
                new ElkLabel { Text = cellName },
            ],
            Ports = [.. ports],
        };

    private static ElkPort Port(
        string id,
        string label,
        string side,
        double x,
        double y) =>
        new()
        {
            Id = id,
            X = x,
            Y = y,
            LayoutOptions = new Dictionary<string, string>
            {
                ["elk.port.side"] = side,
            },
            Labels = [new ElkLabel { Text = label }],
        };

    private static ElkEdge Edge(
        string id,
        int netId,
        string source,
        string target) =>
        new()
        {
            Id = id,
            Sources = [source],
            Targets = [target],
            Labels = [new ElkLabel { Text = $"net{netId}" }],
        };
}
