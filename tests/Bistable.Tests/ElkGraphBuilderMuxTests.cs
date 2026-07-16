using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-4c: <see cref="MuxPrimitive"/> rendering in <see cref="ElkGraphBuilder"/>.
/// Covers single-selector / nested / constant-input / multiple-mux / boundary-wiring cases.
/// </summary>
public sealed class ElkGraphBuilderMuxTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static MuxPrimitive SimpleMux(string output, string sel, params string[] inputs) =>
        new(
            Id: $"mux_{output}_0",
            OutputSignal: output,
            SelectSignals: [sel],
            Inputs: inputs.Select(s => new MuxInput("x", new MuxSignalSource(s))).ToList(),
            Width: 8);

    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    // ── Happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void Mux_TwoInputOneSelector_EmitsMuxNodeWithInOutPorts()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.NotNull(node.Ports);
        // 2 data inputs + 1 selector + 1 output = 4
        Assert.Equal(4, node.Ports!.Count);
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".in.0"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".in.1"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".sel.0"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".out"));
    }

    [Fact]
    public void Mux_WiresDataInputs_FromBoundaryInputs()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.a") &&
            e.Targets.Any(t => t.EndsWith(".in.0")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.b") &&
            e.Targets.Any(t => t.EndsWith(".in.1")));
    }

    [Fact]
    public void Mux_WiresSelector_FromBoundaryInput()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.sel") &&
            e.Targets.Any(t => t.EndsWith(".sel.0")));
    }

    [Fact]
    public void Mux_WiresOutput_ToBoundaryOutputViaContAssign()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");
        DesignContAssign alias = new("y_out", ["y"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("a"), In("b"), In("sel", 1), Out("y_out")],
                [], [], [alias], Primitives: [mux]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".out")) &&
            e.Targets.Contains("boundary_out.y_out"));
    }

    // ── Nested / multi-selector ───────────────────────────────────────────

    [Fact]
    public void Mux_ThreeInputTwoSelectors_EmitsFiveWestPorts()
    {
        // sel1 ? a : (sel0 ? b : c) — flattened into 3 inputs + 2 selectors
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel1", "sel0"],
            Inputs: [
                new("1", new MuxSignalSource("a")),
                new("1", new MuxSignalSource("b")),
                new("0", new MuxSignalSource("c")),
            ],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("a"), In("b"), In("c"), In("sel1", 1), In("sel0", 1), Out("y")],
                [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.NotNull(node.Ports);
        // 3 data inputs + 2 selectors + 1 output = 6
        Assert.Equal(6, node.Ports!.Count);
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".sel.1"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".in.2"));
    }

    // ── Constant input edge case ──────────────────────────────────────────

    [Fact]
    public void Mux_ConstantInOneBranch_NoEdgeForConstButPortExists()
    {
        // sel ? a : 8'h0  — constant in false branch; port still present, no edge to constant.
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [
                new("1", new MuxSignalSource("a")),
                new("0", new MuxConstantSource("0", 8)),
            ],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        // Both inputs ports still created (in.0 for signal, in.1 for constant)
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".in.0"));
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".in.1"));

        // Edge for the signal input exists; constant input has no incoming edge.
        int edgesIntoMux = result.Graph.Edges.Count(e =>
            e.Targets.Any(t => t.StartsWith("mux_y") && t.Contains(".in.")));
        Assert.Equal(1, edgesIntoMux);
    }

    // ── Multiple muxes in one scope ───────────────────────────────────────

    [Fact]
    public void TwoMuxes_EachGetsOwnNode_AndWiresIndependently()
    {
        MuxPrimitive mux1 = SimpleMux("y1", "sel", "a", "b");
        MuxPrimitive mux2 = SimpleMux("y2", "sel", "c", "d");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("a"), In("b"), In("c"), In("d"), In("sel", 1), Out("y1"), Out("y2")],
                [], [], [], Primitives: [mux1, mux2]),
            compactLayout: true);

        Assert.Equal(2, result.Graph.Children.Count(n => ElkNodeIds.IsMux(n.Id)));
        Assert.Single(result.Graph.Children, n => n.Id == "mux_y1");
        Assert.Single(result.Graph.Children, n => n.Id == "mux_y2");
    }

    // ── Orphan input (no producer) — must not throw, must not add edge ─────

    [Fact]
    public void Mux_InputWithNoProducer_NoEdgeNoCrash()
    {
        // "orphan" has no boundary port / contassign / primitive producing it
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [
                new("1", new MuxSignalSource("a")),
                new("0", new MuxSignalSource("orphan")),
            ],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("sel", 1), Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".in.1"));
        Assert.DoesNotContain(result.Graph.Edges, e =>
            e.Targets.Any(t => t.EndsWith(".in.1")));
    }

    // ── Empty selectors / inputs edge case ────────────────────────────────

    [Fact]
    public void Mux_NoInputs_StillCreatesNodeWithOnlyOutput()
    {
        // Degenerate case — defensive guard
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: [],
            Inputs: [],
            Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([Out("y")], [], [], [], Primitives: [mux]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.NotNull(node.Ports);
        Assert.Single(node.Ports!);   // only output port
        Assert.EndsWith(".out", node.Ports![0].Id);
    }

    [Fact]
    public void Mux_SelectorPort_LayoutOptions_HasPortSideSouth()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkNode node = BuildSingleMux(mux, [In("a"), In("b"), In("sel", 1), Out("y")]);
        ElkPort selector = Assert.Single(node.Ports!, p => p.Id.EndsWith(".sel.0"));

        Assert.Equal("SOUTH", selector.LayoutOptions!["elk.port.side"]);
    }

    [Fact]
    public void Mux_DataInputPort_LayoutOptions_HasPortSideWest()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkNode node = BuildSingleMux(mux, [In("a"), In("b"), In("sel", 1), Out("y")]);
        ElkPort input = Assert.Single(node.Ports!, p => p.Id.EndsWith(".in.0"));

        Assert.Equal("WEST", input.LayoutOptions!["elk.port.side"]);
    }

    [Fact]
    public void Mux_Height_BasedOnDataInputsOnly_NotSelectorCount()
    {
        MuxPrimitive oneSelector = new(
            "mux_y_0", "y",
            SelectSignals: ["s0"],
            Inputs: [new("a", new MuxSignalSource("a")), new("b", new MuxSignalSource("b"))],
            Width: 8);
        MuxPrimitive fourSelectors = oneSelector with { SelectSignals = ["s0", "s1", "s2", "s3"] };

        ElkNode one = BuildSingleMux(oneSelector, [In("a"), In("b"), In("s0", 1), Out("y")]);
        ElkNode four = BuildSingleMux(fourSelectors, [In("a"), In("b"), In("s0", 1), In("s1", 1), In("s2", 1), In("s3", 1), Out("y")]);

        Assert.Equal(one.Height, four.Height);
    }

    [Fact]
    public void Mux_Width_GrowsWithSelectorCount()
    {
        MuxPrimitive oneSelector = SimpleMux("y", "s0", "a", "b");
        MuxPrimitive fourSelectors = oneSelector with { SelectSignals = ["s0", "s1", "s2", "s3"] };

        ElkNode one = BuildSingleMux(oneSelector, [In("a"), In("b"), In("s0", 1), Out("y")]);
        ElkNode four = BuildSingleMux(fourSelectors, [In("a"), In("b"), In("s0", 1), In("s1", 1), In("s2", 1), In("s3", 1), Out("y")]);

        Assert.True(four.Width > one.Width);
    }

    [Fact]
    public void LongOutputSignalName_MuxWidth_AccommodatesTitle()
    {
        string output = "very_long_mux_output_signal";
        MuxPrimitive mux = SimpleMux(output, "sel", "a", "b");

        ElkNode node = BuildSingleMux(mux, [In("a"), In("b"), In("sel", 1), Out(output)]);

        Assert.True(node.Width >= ("MUX " + output).Length * 7 + 16);
    }

    [Fact]
    public void DiagnosticInputLabel_MuxWidth_DoesNotOverExpand()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [
                new("control_pins[14:8]·X", new MuxConstantSource("X", 1)),
                new("else", new MuxSignalSource("b")),
            ],
            Width: 8);

        ElkNode node = BuildSingleMux(mux, [In("sel", 1), In("b"), Out("y")]);

        Assert.Equal(94, node.Width);
    }

    [Fact]
    public void NormalMux_Width_StaysCompact()
    {
        MuxPrimitive mux = SimpleMux("y", "sel", "a", "b");

        ElkNode node = BuildSingleMux(mux, [In("a"), In("b"), In("sel", 1), Out("y")]);

        Assert.Equal(94, node.Width);
    }

    private static ElkNode BuildSingleMux(MuxPrimitive mux, IReadOnlyList<HierarchyScopePortViewModel> ports)
    {
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(ports, [], [], [], Primitives: [mux]),
            compactLayout: true);
        return Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
    }
}
