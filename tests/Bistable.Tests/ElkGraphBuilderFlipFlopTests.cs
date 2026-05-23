using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-4: validates the additive FF rendering path in <see cref="ElkGraphBuilder"/>.
/// When <see cref="ElkScopeData.Primitives"/> contains <see cref="FlipFlopPrimitive"/> entries,
/// the builder must emit FF nodes and wire D/Clk/Rst/Q ports to the matching signal endpoints.
/// </summary>
public sealed class ElkGraphBuilderFlipFlopTests
{
    [Fact]
    public void FlipFlop_BasicPosedge_EmitsFlipFlopNodeWithDClkQPorts()
    {
        HierarchyScopePortViewModel clk  = new("clk",  SignalDirection.Input,  1, false);
        HierarchyScopePortViewModel dIn  = new("d_in", SignalDirection.Input,  8, false);
        HierarchyScopePortViewModel qOut = new("q",    SignalDirection.Output, 8, false);

        FlipFlopPrimitive ff = new(
            Id: "ff_q_0",
            QSignal: "q",
            ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: null,
            AsyncResetEdge: null,
            DSignal: "d_in",
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, dIn, qOut], [], [], [], ExpandedPaths: null, Primitives: [ff]),
            compactLayout: true);

        ElkNode ffNode = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.NotNull(ffNode.Ports);
        Assert.Equal(3, ffNode.Ports!.Count);   // D, Clk, Q (no reset)
        Assert.Contains(ffNode.Ports, p => p.Id.EndsWith(".d"));
        Assert.Contains(ffNode.Ports, p => p.Id.EndsWith(".clk"));
        Assert.Contains(ffNode.Ports, p => p.Id.EndsWith(".q"));
    }

    [Fact]
    public void FlipFlop_WithAsyncReset_AddsResetPort()
    {
        HierarchyScopePortViewModel clk   = new("clk",   SignalDirection.Input,  1, false);
        HierarchyScopePortViewModel rstN  = new("rst_n", SignalDirection.Input,  1, false);
        HierarchyScopePortViewModel dIn   = new("d_in",  SignalDirection.Input,  8, false);
        HierarchyScopePortViewModel qOut  = new("q",     SignalDirection.Output, 8, false);

        FlipFlopPrimitive ff = new(
            Id: "ff_q_0",
            QSignal: "q",
            ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: "rst_n",
            AsyncResetEdge: EdgeKind.Falling,
            DSignal: "d_in",
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, rstN, dIn, qOut], [], [], [], ExpandedPaths: null, Primitives: [ff]),
            compactLayout: true);

        ElkNode ffNode = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.NotNull(ffNode.Ports);
        Assert.Equal(4, ffNode.Ports!.Count);   // D, Clk, Rst, Q
        Assert.Contains(ffNode.Ports, p => p.Id.EndsWith(".rst"));
    }

    [Fact]
    public void FlipFlop_WiresBoundaryInputToD()
    {
        HierarchyScopePortViewModel clk  = new("clk",  SignalDirection.Input,  1, false);
        HierarchyScopePortViewModel dIn  = new("d_in", SignalDirection.Input,  8, false);

        FlipFlopPrimitive ff = new(
            "ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, dIn], [], [], [], Primitives: [ff]),
            compactLayout: true);

        // An edge should exist: boundary_in.d_in → ff_q.d
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.d_in") &&
            e.Targets.Any(t => t.EndsWith(".d")));
    }

    [Fact]
    public void FlipFlop_WiresBoundaryClockToClk()
    {
        HierarchyScopePortViewModel clk = new("clk", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel dIn = new("d_in", SignalDirection.Input, 8, false);

        FlipFlopPrimitive ff = new(
            "ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, dIn], [], [], [], Primitives: [ff]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.clk") &&
            e.Targets.Any(t => t.EndsWith(".clk")));
    }

    [Fact]
    public void FlipFlop_QOutput_DrivesContAssignAlias()
    {
        // q is a register; assign q_out = q (alias) — Q should drive boundary output q_out.
        HierarchyScopePortViewModel clk  = new("clk",   SignalDirection.Input,  1, false);
        HierarchyScopePortViewModel dIn  = new("d_in",  SignalDirection.Input,  8, false);
        HierarchyScopePortViewModel qOut = new("q_out", SignalDirection.Output, 8, false);
        DesignContAssign alias = new("q_out", ["q"]);

        FlipFlopPrimitive ff = new(
            "ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, dIn, qOut], [], [], [alias], Primitives: [ff]),
            compactLayout: true);

        // Edge: ff_q.q → boundary_out.q_out (via the q wire alias)
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".q")) &&
            e.Targets.Contains("boundary_out.q_out"));
    }

    [Fact]
    public void NoPrimitives_LegacyBehaviorUnchanged()
    {
        // Regression guard: when Primitives is null/empty, the builder behaves exactly as before.
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 32, false);
        var portConns = new[]
        {
            new HierarchyScopeInstancePortConnectionViewModel("opcode", "opcode", isInput: true, width: 7)
        };
        HierarchyScopeInstanceViewModel control = new(
            "top.u_control", "u_control", "u_control_module",
            inputCount: 1, outputCount: 0,
            exactSignalCount: 0, descendantSignalCount: 0,
            portConns);
        DesignContAssign assign = new("opcode", ["instruction"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction], [control], [], [assign]),
            compactLayout: true);

        // No FF nodes; just the legacy contassign routing
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsFlipFlop(n.Id));
        Assert.Single(result.Graph.Edges);
    }
}
