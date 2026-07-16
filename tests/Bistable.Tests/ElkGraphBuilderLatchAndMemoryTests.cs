using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-4c: <see cref="LatchPrimitive"/> and <see cref="MemoryPrimitive"/> rendering.
/// </summary>
public sealed class ElkGraphBuilderLatchAndMemoryTests
{
    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    // ── Latch happy path ──────────────────────────────────────────────────

    [Fact]
    public void Latch_Basic_EmitsLatchNodeWithDGQPorts()
    {
        LatchPrimitive latch = new(
            "latch_q_0", "q", GateSignal: "g", DSignal: "d", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("d"), In("g", 1), Out("q")], [], [], [], Primitives: [latch]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsLatch(n.Id));
        Assert.NotNull(node.Ports);
        Assert.Equal(3, node.Ports!.Count);
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".d"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".g"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".q"));
    }

    [Fact]
    public void Latch_WiresBoundaryToD()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "g", "d", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("d"), In("g", 1)], [], [], [], Primitives: [latch]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.d") &&
            e.Targets.Any(t => t.EndsWith(".d")));
    }

    [Fact]
    public void Latch_WiresGateSignal()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "g", "d", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("d"), In("g", 1)], [], [], [], Primitives: [latch]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.g") &&
            e.Targets.Any(t => t.EndsWith(".g")));
    }

    [Fact]
    public void Latch_QDrivesContAssignAlias_ToBoundaryOutput()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "g", "d", 8);
        DesignContAssign alias = new("q_out", ["q"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("d"), In("g", 1), Out("q_out")],
                [], [], [alias], Primitives: [latch]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".q")) &&
            e.Targets.Contains("boundary_out.q_out"));
    }

    // ── Memory happy path ─────────────────────────────────────────────────

    [Fact]
    public void Memory_EmitsMemoryNodeWithDepthLabel()
    {
        MemoryPrimitive mem = new(
            "mem_arr_0", SignalName: "arr", CellWidth: 8, DepthHi: 15, DepthLo: 0);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [mem]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
        Assert.NotNull(node.Labels);
        Assert.Contains("arr", node.Labels![0].Text);
        Assert.Contains("[15:0]", node.Labels![0].Text);
        Assert.Contains("×8", node.Labels![0].Text);
    }

    [Fact]
    public void Memory_HasReadOutAndWriteInPorts()
    {
        // The tile is the canonical read source (EAST .dout) and takes writes on a
        // WEST .win port. In isolation both are unconnected but the ports exist.
        MemoryPrimitive mem = new("mem_arr_0", "arr", 8, 15, 0);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [mem]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
        Assert.NotNull(node.Ports);
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".win"));
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".dout"));
        Assert.Empty(result.Graph.Edges); // nothing to wire in isolation
    }

    [Fact]
    public void Memory_ReadOut_DrivesMemoryReadSource()
    {
        // MEM tile read-out (produces `mem`) → RD-mem source input (consumes `mem`).
        MemoryPrimitive mem = new("mem_mem_0", "mem", CellWidth: 8, DepthHi: 15, DepthLo: 0);
        MemoryReadPrimitive read = new(
            "memrd_data_0", MemorySignal: "mem", AddressSignal: "addr", OutputSignal: "data", CellWidth: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("addr", 4), Out("data", 8)], [], [], [], Primitives: [mem, read]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".dout")) &&
            e.Targets.Any(t => t.EndsWith(".src")));
    }

    [Fact]
    public void MemoryWriteFF_WiresToMemoryWriteIn_AndDoesNotDoubleDriveMem()
    {
        // A clocked `mem[addr] <= data` decodes to a FF with QSignal = "mem".
        // Its Q must drive the tile write-in, NOT the array signal directly — so a
        // reader consumes `mem` only from the tile, never from the FF.
        MemoryPrimitive mem = new("mem_mem_0", "mem", CellWidth: 8, DepthHi: 15, DepthLo: 0);
        FlipFlopPrimitive writeFf = new(
            "ff_mem_0", "mem",
            ClockSignal: "clk", ClockEdge: EdgeKind.Rising,
            AsyncResetSignal: null, AsyncResetEdge: null,
            DSignal: "wdata", Width: 8);
        MemoryReadPrimitive read = new(
            "memrd_rdata_0", MemorySignal: "mem", AddressSignal: "raddr", OutputSignal: "rdata", CellWidth: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("clk", 1), In("wdata", 8), In("raddr", 4), Out("rdata", 8)],
                [], [], [], Primitives: [mem, writeFf, read]),
            compactLayout: true);

        // FF.Q → tile .win
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("ff_mem") && s.EndsWith(".q")) &&
            e.Targets.Any(t => t.EndsWith(".win")));
        // The reader's source is driven by the tile, not by the FF's Q.
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".dout")) &&
            e.Targets.Any(t => t.EndsWith(".src")));
        Assert.DoesNotContain(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("ff_mem") && s.EndsWith(".q")) &&
            e.Targets.Any(t => t.EndsWith(".src")));
    }

    [Fact]
    public void Memory_DepthOne_StillRenders()
    {
        MemoryPrimitive mem = new("mem_single_0", "single", CellWidth: 4, DepthHi: 0, DepthLo: 0);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [mem]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
        Assert.Equal(1, mem.Depth);
        Assert.Contains("[0:0]", node.Labels![0].Text);
    }

    [Fact]
    public void MemoryRead_EmitsReadNodeWithAddressAndDataPorts()
    {
        MemoryReadPrimitive read = new(
            "memrd_data_0",
            MemorySignal: "mem",
            AddressSignal: "addr",
            OutputSignal: "data",
            CellWidth: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("addr", 4), Out("data", 8)], [], [], [], Primitives: [read]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemoryRead(n.Id));
        Assert.NotNull(node.Ports);
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".addr"));
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".data"));
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".src")); // source-memory input
        Assert.Contains("RD mem", node.Labels![0].Text);
        // Only title + output name; the redundant third (memory-name) label is gone
        // so it can't overlap the live-value badge.
        Assert.Equal(2, node.Labels!.Count);
    }

    [Fact]
    public void MemoryRead_WiresAddressAndData()
    {
        MemoryReadPrimitive read = new(
            "memrd_data_0",
            MemorySignal: "mem",
            AddressSignal: "addr",
            OutputSignal: "data",
            CellWidth: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("addr", 4), Out("data", 8)], [], [], [], Primitives: [read]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.addr") &&
            e.Targets.Any(t => t.EndsWith(".addr")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".data")) &&
            e.Targets.Contains("boundary_out.data"));
    }
}
