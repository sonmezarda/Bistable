using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

public sealed class ElkGraphBuilderTests
{
    [Fact]
    public void RoutesBoundaryInputThroughContinuousAssignmentAlias()
    {
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 32, isSigned: false);
        HierarchyScopeInstanceViewModel control = Child(
            "top.u_control",
            "u_control",
            [new HierarchyScopeInstancePortConnectionViewModel("opcode", "opcode", isInput: true, width: 7)]);
        DesignContAssign assign = new("opcode", ["instruction"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction], [control], [], [assign]),
            compactLayout: true);

        ElkEdge edge = Assert.Single(result.Graph.Edges);
        Assert.Equal("boundary_in.instruction", Assert.Single(edge.Sources));
        Assert.Equal("child_top_u_control.in.opcode", Assert.Single(edge.Targets));
        Assert.NotNull(edge.Labels);
        Assert.Equal("instruction", edge.Labels[0].Text);
        Assert.Equal("7", edge.Labels[1].Text);
    }

    [Fact]
    public void RoutesBoundaryOutputThroughContinuousAssignmentAlias()
    {
        HierarchyScopePortViewModel memAddr = new("mem_addr", SignalDirection.Output, 32, isSigned: false);
        HierarchyScopeInstanceViewModel alu = Child(
            "top.u_alu",
            "u_alu",
            [new HierarchyScopeInstancePortConnectionViewModel("addr", "alu_addr", isInput: false, width: 32)]);
        DesignContAssign assign = new("mem_addr", ["alu_addr"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([memAddr], [alu], [], [assign]),
            compactLayout: true);

        ElkEdge edge = Assert.Single(result.Graph.Edges);
        Assert.Equal("child_top_u_alu.out.addr", Assert.Single(edge.Sources));
        Assert.Equal("boundary_out.mem_addr", Assert.Single(edge.Targets));
        Assert.NotNull(edge.Labels);
        Assert.Equal("alu_addr", edge.Labels[0].Text);
        Assert.Equal("32", edge.Labels[1].Text);
    }

    [Fact]
    public void RoutesCombinationalFanInThroughOperatorNode()
    {
        // assign mem_addr = pc + instruction  — two contributing signals
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 32, isSigned: false);
        HierarchyScopePortViewModel memAddr = new("mem_addr", SignalDirection.Output, 32, isSigned: false);
        HierarchyScopeInstanceViewModel registers = Child(
            "top.u_registers",
            "u_registers",
            [new HierarchyScopeInstancePortConnectionViewModel("pc", "pc", isInput: false, width: 32)]);
        DesignContAssign assign = new("mem_addr", ["pc", "instruction"], "+");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction, memAddr], [registers], [], [assign]),
            compactLayout: true);

        // An operator node "op_mem_addr" must be present.
        ElkNode opNode = Assert.Single(result.Graph.Children, n => n.Id == "op_mem_addr");
        Assert.NotNull(opNode.Ports);
        Assert.Equal(3, opNode.Ports!.Count);
        Assert.Contains(opNode.Ports, p => p.Id == "op_mem_addr.in.0");
        Assert.Contains(opNode.Ports, p => p.Id == "op_mem_addr.in.1");
        Assert.Contains(opNode.Ports, p => p.Id == "op_mem_addr.out");

        // Exactly 3 edges: sources → operator inputs, operator output → boundary.
        Assert.Equal(3, result.Graph.Edges.Count);
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("child_top_u_registers.out.pc") &&
            e.Targets.Contains("op_mem_addr.in.0"));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.instruction") &&
            e.Targets.Contains("op_mem_addr.in.1"));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("op_mem_addr.out") &&
            e.Targets.Contains("boundary_out.mem_addr"));
    }

    [Fact]
    public void OperatorNodeCarriesSymbolLabel()
    {
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel b = new("b", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel result_port = new("result", SignalDirection.Output, 8, isSigned: false);
        DesignContAssign assign = new("result", ["a", "b"], "^");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, b, result_port], [], [], [assign]),
            compactLayout: true);

        ElkNode opNode = Assert.Single(result.Graph.Children, n => n.Id == "op_result");
        Assert.NotNull(opNode.Labels);
        Assert.Equal("^", opNode.Labels[0].Text);
    }

    [Fact]
    public void OperatorNodeFallsBackToQuestionMarkWhenSymbolIsNull()
    {
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 1, isSigned: false);
        HierarchyScopePortViewModel b = new("b", SignalDirection.Input, 1, isSigned: false);
        HierarchyScopePortViewModel y = new("y", SignalDirection.Output, 1, isSigned: false);
        DesignContAssign assign = new("y", ["a", "b"], null);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, b, y], [], [], [assign]),
            compactLayout: true);

        ElkNode opNode = Assert.Single(result.Graph.Children, n => n.Id == "op_y");
        Assert.Equal("?", opNode.Labels![0].Text);
    }

    [Fact]
    public void RoutesBusSplitterToSliceConsumers()
    {
        // assign opcode = instruction[15:12];  assign imm = instruction[7:0]
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 16, isSigned: false);
        HierarchyScopeInstanceViewModel control = Child(
            "top.u_ctrl", "u_ctrl",
            [new HierarchyScopeInstancePortConnectionViewModel("opcode", "opcode", isInput: true, width: 4)]);
        HierarchyScopeInstanceViewModel alu = Child(
            "top.u_alu", "u_alu",
            [new HierarchyScopeInstancePortConnectionViewModel("imm", "imm", isInput: true, width: 8)]);
        DesignContAssign opcodeAssign = new("opcode", ["instruction"], null, new DesignBitRange(15, 12));
        DesignContAssign immAssign = new("imm", ["instruction"], null, new DesignBitRange(7, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction], [control, alu], [], [opcodeAssign, immAssign]),
            compactLayout: true);

        // One splitter node for the instruction bus
        ElkNode splitter = Assert.Single(result.Graph.Children, n => n.Id == "split_instruction");
        Assert.NotNull(splitter.Ports);
        Assert.Equal(3, splitter.Ports!.Count);
        Assert.Contains(splitter.Ports, p => p.Id == "split_instruction.in");

        // Output ports carry bit-range labels; MSB-first ordering ([15:12] before [7:0])
        ElkPort highPort = Assert.Single(splitter.Ports, p => p.Labels is { Count: > 0 } && p.Labels[0].Text == "[15:12]");
        ElkPort lowPort = Assert.Single(splitter.Ports, p => p.Labels is { Count: > 0 } && p.Labels[0].Text == "[7:0]");
        Assert.True(splitter.Ports.IndexOf(highPort) < splitter.Ports.IndexOf(lowPort));

        // 3 edges: bus → splitter.in, splitter.out → each consumer
        Assert.Equal(3, result.Graph.Edges.Count);
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.instruction") && e.Targets.Contains("split_instruction.in"));
        Assert.Contains(result.Graph.Edges, e =>
            e.Targets.Contains("child_top_u_ctrl.in.opcode"));
        Assert.Contains(result.Graph.Edges, e =>
            e.Targets.Contains("child_top_u_alu.in.imm"));
    }

    [Fact]
    public void SingleSliceStillCreatesSplitterNode()
    {
        // A single sel assign still produces a splitter (shows the bit range, not just a wire)
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopeInstanceViewModel child = Child(
            "top.u_x", "u_x",
            [new HierarchyScopeInstancePortConnectionViewModel("lo", "lo", isInput: true, width: 4)]);
        DesignContAssign assign = new("lo", ["bus"], null, new DesignBitRange(3, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([bus], [child], [], [assign]),
            compactLayout: true);

        ElkNode splitter = Assert.Single(result.Graph.Children, n => n.Id == "split_bus");
        Assert.Equal(2, splitter.Ports!.Count);
        Assert.Contains(splitter.Ports, p => p.Labels is { Count: > 0 } && p.Labels[0].Text == "[3:0]");
    }

    private static HierarchyScopeInstanceViewModel Child(
        string hierarchyPath,
        string instanceName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports) =>
        new(
            hierarchyPath,
            instanceName,
            "module",
            ports.Count(static port => port.IsInput),
            ports.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            ports);
}
