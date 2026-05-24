using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
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
    public void Memory_NoEdges_NoPorts()
    {
        // Memory tiles currently have no port plumbing — array access is a Phase 2+ follow-up.
        MemoryPrimitive mem = new("mem_arr_0", "arr", 8, 15, 0);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [mem]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMemory(n.Id));
        // Ports collection exists but is empty — no edges yet.
        Assert.True(node.Ports is null || node.Ports.Count == 0);
        Assert.Empty(result.Graph.Edges);
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
}
