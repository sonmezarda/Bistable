using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

public sealed class ElkGraphBuilderOrphanPrimitivePruningTests
{
    [Fact]
    public void OrphanEqualNode_GetsPruned()
    {
        DesignContAssign equal = new("is_push_instr", ["opcode", "push_opcode"], "==");

        ElkBuildResult result = Build(ports: [], contAssigns: [equal]);

        Assert.DoesNotContain(result.Graph.Children, n => n.Id == "op_is_push_instr");
    }

    [Fact]
    public void ConnectedEqualNode_NotPruned()
    {
        DesignContAssign equal = new("is_push_instr", ["opcode", "push_opcode"], "==");

        ElkBuildResult result = Build(
            ports: [In("opcode"), In("push_opcode"), Out("is_push_instr", 1)],
            contAssigns: [equal]);

        Assert.Contains(result.Graph.Children, n => n.Id == "op_is_push_instr");
    }

    [Fact]
    public void OrphanFlipFlop_GetsPruned_OnlyIfBothDAndQUnconnected()
    {
        FlipFlopPrimitive ff = new(
            "ff_q_0", "q",
            ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: null,
            AsyncResetEdge: null,
            DSignal: "d",
            Width: 8);

        ElkBuildResult result = Build(ports: [], primitives: [ff]);

        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
    }

    [Fact]
    public void WriteOnlyFlipFlop_QUnconnected_ButDConnected_NotPruned()
    {
        FlipFlopPrimitive ff = new(
            "ff_q_0", "q",
            ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: null,
            AsyncResetEdge: null,
            DSignal: "d",
            Width: 8);

        ElkBuildResult result = Build(ports: [In("d"), In("clk", 1)], primitives: [ff]);

        Assert.Contains(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
    }

    [Fact]
    public void BoundaryNode_NeverPruned()
    {
        ElkBuildResult result = Build(ports: [In("unused")]);

        Assert.Contains(result.Graph.Children, n => n.Id == "boundary_in");
    }

    [Fact]
    public void MemoryNode_NeverPruned()
    {
        MemoryPrimitive memory = new("mem_ram", "ram", CellWidth: 8, DepthHi: 15, DepthLo: 0);

        ElkBuildResult result = Build(ports: [], primitives: [memory]);

        Assert.Contains(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
    }

    [Fact]
    public void HalfWiredOperator_InputsConnectedButOutputUnconsumed_GetsPruned()
    {
        // Inputs are driven by boundary inputs, but the result feeds no consumer
        // (no matching boundary output / downstream). This is the floating
        // half-wired gate the user saw (photos 2/5) — it must be pruned.
        DesignContAssign equal = new("dangling_eq", ["opcode", "push_opcode"], "==");

        ElkBuildResult result = Build(
            ports: [In("opcode"), In("push_opcode")],
            contAssigns: [equal]);

        Assert.DoesNotContain(result.Graph.Children, n => n.Id == "op_dangling_eq");
    }

    [Fact]
    public void OrphanConstantTie_GetsPruned()
    {
        // A constant driving nobody must not float in the scope.
        ConstantTiePrimitive tie = new("tie_dead_0", "dead_net", "8'h00", 8);

        ElkBuildResult result = Build(ports: [], primitives: [tie]);

        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsConstantTie(n.Id));
    }

    private static ElkBuildResult Build(
        IReadOnlyList<HierarchyScopePortViewModel> ports,
        IReadOnlyList<DesignContAssign>? contAssigns = null,
        IReadOnlyList<SchematicPrimitive>? primitives = null)
    {
        return new ElkGraphBuilder().Build(
            new ElkScopeData(ports, [], [], contAssigns ?? [], Primitives: primitives),
            compactLayout: true);
    }

    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);
}
