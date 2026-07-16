using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2.5 P2.5-3: when a primitive owns a target signal, the legacy
/// contassign-driven AddOperatorNode / AddJoinerNode for the SAME target must
/// be suppressed. Without this, every ternary contassign produces a duplicate
/// "?:" operator box next to its proper MUX trapezoid; every concat produces
/// a duplicate "{}" box next to its proper JOIN wedge; etc.
///
/// Phase 2.4d already covered Gate/Arith. P2.5-3 extends suppression to
/// Mux, Buffer, Inverter, Joiner.
/// </summary>
public sealed class ElkGraphBuilderPrimitiveSuppressionTests
{
    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    // ── Mux suppresses legacy "?:" operator node ─────────────────────────

    [Fact]
    public void MuxPrimitive_SuppressesLegacyCondOperator()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);
        // The legacy contassign that the parser would have produced for the same target:
        DesignContAssign legacyCond = new("y", ["sel", "a", "b"], "?:");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("sel", 1), In("a"), In("b")], [], [], [legacyCond], Primitives: [mux]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id) && n.Id == "op_y");
    }

    // ── Buffer suppresses legacy operator-node (defensive — legacy normally skips single-source) ──

    [Fact]
    public void BufferPrimitive_SuppressesLegacyOperatorForSameTarget()
    {
        BufferPrimitive buf = new("buf_y_0", "y", "a", Width: 8);
        // Even though the legacy AddOperatorNode skips single-source by default,
        // a primitive should claim its target so any future change to the legacy
        // path can't accidentally produce a duplicate.
        DesignContAssign legacyAlias = new("y", ["a"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a")], [], [], [legacyAlias], Primitives: [buf]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsBuffer(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id));
    }

    // ── Inverter suppresses legacy "~" operator node ─────────────────────

    [Fact]
    public void InverterPrimitive_SuppressesLegacyUnaryNotOperator()
    {
        InverterPrimitive inv = new("inv_y_0", "y", "x", Width: 8);
        // Verilator's <not><varref name="x"/></not> would flatten to a multi-source
        // contassign with OperatorSymbol="~" ONLY IF source count >= 2. For a
        // single-input inverter, legacy doesn't generate a node — but if it ever
        // started to, the primitive must own the target.
        DesignContAssign legacyNot = new("y", ["x", "x"], "~");   // synthetic multi-source

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("x")], [], [], [legacyNot], Primitives: [inv]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsInverter(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id));
    }

    /// <summary>
    /// Joiner NOT suppressed — legacy is the only renderer for joiners.
    /// The builder's primitive switch does NOT handle JoinerPrimitive; legacy
    /// AddJoinerNode is the canonical renderer for concat contassigns. Suppressing
    /// the legacy path here would erase the joiner from the graph entirely.
    /// </summary>
    [Fact]
    public void JoinerPrimitive_DoesNotSuppressLegacy_LegacyIsTheOnlyJoinerRenderer()
    {
        JoinerPrimitive join = new("join_result_0", "result", ["hi", "lo"], OutputWidth: 16);
        DesignContAssign legacyConcat = new("result", ["hi", "lo"], "{}");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("hi"), In("lo")], [], [], [legacyConcat], Primitives: [join]),
            compactLayout: true);

        // Legacy joiner node MUST exist — that's the visible joiner.
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsJoiner(n.Id) && n.Id == "join_result");
    }

    // ── Mixed scope: each primitive type suppresses only its own target ──

    [Fact]
    public void MixedPrimitives_EachSuppressesOnlyItsOwnTarget()
    {
        MuxPrimitive mux = new(
            "mux_a_0", "a",
            SelectSignals: ["s"],
            Inputs: [new("1", new MuxSignalSource("x")), new("0", new MuxSignalSource("y"))],
            Width: 8);
        BufferPrimitive buf = new("buf_b_0", "b", "x", 8);

        // Legacy contassigns for the SAME targets — must be suppressed.
        DesignContAssign assignA = new("a", ["s", "x", "y"], "?:");
        DesignContAssign assignB = new("b", ["x"]);
        // Untouched target — must still produce a legacy operator node.
        DesignContAssign assignC = new("c", ["x", "y"], "+");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("s", 1), In("x"), In("y"), Out("a"), Out("b"), Out("c")],
                [], [],
                [assignA, assignB, assignC],
                Primitives: [mux, buf]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsMux(n.Id));
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsBuffer(n.Id));
        // op_c IS allowed (no primitive owns c)
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id) && n.Id == "op_c");
        // But NOT op_a (suppressed by Mux) and NOT op_b (suppressed by Buffer)
        Assert.DoesNotContain(result.Graph.Children, n => n.Id == "op_a");
        Assert.DoesNotContain(result.Graph.Children, n => n.Id == "op_b");
    }

    // ── Regression: empty primitives → no suppression ─────────────────────

    [Fact]
    public void NoPrimitives_LegacyOperatorAndJoinerStillProduceNodes()
    {
        DesignContAssign assignOp = new("y", ["a", "b"], "+");
        DesignContAssign assignConcat = new("result", ["hi", "lo"], "{}");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [In("a"), In("b"), In("hi"), In("lo"), Out("y"), Out("result")],
                [], [], [assignOp, assignConcat]),   // no Primitives
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id) && n.Id == "op_y");
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsJoiner(n.Id) && n.Id == "join_result");
    }
}
