using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-4d: Buffer / Inverter / Gate / Arith primitives rendered through
/// the primitive path. Covers happy-path edges, port labelling, the legacy-path
/// suppression contract (multi-source contassigns owned by Gate/Arith primitives
/// must NOT also produce an "op_..." operator node), and no-regression guards.
/// </summary>
public sealed class ElkGraphBuilderCombinationalPrimitiveTests
{
    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    // ── Buffer ───────────────────────────────────────────────────────────

    [Fact]
    public void Buffer_EmitsBufferNodeWithInOutPortsAndAYLabels()
    {
        BufferPrimitive buf = new("buf_y_0", "y", "a", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), Out("y")], [], [], [], Primitives: [buf]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsBuffer(n.Id));
        Assert.Equal("buf_y", node.Id);
        Assert.NotNull(node.Ports);
        Assert.Equal(2, node.Ports!.Count);
        Assert.Equal("A", Assert.Single(node.Ports.Single(p => p.Id.EndsWith(".in")).Labels!).Text);
        Assert.Equal("Y", Assert.Single(node.Ports.Single(p => p.Id.EndsWith(".out")).Labels!).Text);
    }

    [Fact]
    public void Buffer_WiresInputAndOutput()
    {
        BufferPrimitive buf = new("buf_y_0", "y", "a", 8);
        DesignContAssign alias = new("y_out", ["y"]);   // forces y to have a downstream consumer

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), Out("y_out")], [], [], [alias], Primitives: [buf]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.a") && e.Targets.Any(t => t.EndsWith(".in")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".out")) && e.Targets.Contains("boundary_out.y_out"));
    }

    // ── Inverter ─────────────────────────────────────────────────────────

    [Fact]
    public void Inverter_EmitsInverterNode()
    {
        InverterPrimitive inv = new("inv_y_0", "y", "x", Width: 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("x"), Out("y")], [], [], [], Primitives: [inv]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsInverter(n.Id));
        Assert.Equal("inv_y", node.Id);
        Assert.Equal(2, node.Ports!.Count);
    }

    [Fact]
    public void Inverter_WiresInput()
    {
        InverterPrimitive inv = new("inv_y_0", "y", "x", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("x")], [], [], [], Primitives: [inv]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.x") && e.Targets.Any(t => t.EndsWith(".in")));
    }

    // ── Gate ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(GateKind.And)]
    [InlineData(GateKind.Or)]
    [InlineData(GateKind.Xor)]
    [InlineData(GateKind.ReduceAnd)]
    public void Gate_EmitsGateNode_RegardlessOfKind(GateKind kind)
    {
        int inputCount = kind is GateKind.ReduceAnd or GateKind.ReduceOr or GateKind.ReduceXor ? 1 : 2;
        var inputs = Enumerable.Range(0, inputCount).Select(i => $"a{i}").ToList();
        GatePrimitive gate = new("gate_y_0", "y", kind, inputs, Width: 8);

        var boundary = inputs.Select(n => In(n)).Concat([Out("y")]).ToList();
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(boundary, [], [], [], Primitives: [gate]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsGate(n.Id));
        // Label embeds kind for the renderer to pick the right body shape
        Assert.StartsWith(kind.ToString(), node.Labels![0].Text);
    }

    [Fact]
    public void Gate_TwoInput_PortsLabeledAB()
    {
        GatePrimitive gate = new("gate_y_0", "y", GateKind.And, ["a", "b"], 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [], Primitives: [gate]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsGate(n.Id));
        Assert.Equal("A", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".in.0")).Labels!).Text);
        Assert.Equal("B", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".in.1")).Labels!).Text);
        Assert.Equal("Y", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".out")).Labels!).Text);
    }

    [Fact]
    public void Gate_ThreeInput_PortsLabeledI0_I1_I2()
    {
        // Wide gates (>2 inputs) get numeric labels — the AB scheme stops at 2.
        GatePrimitive gate = new("gate_y_0", "y", GateKind.And, ["a", "b", "c"], 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b"), In("c")], [], [], [], Primitives: [gate]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsGate(n.Id));
        Assert.Equal("I0", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".in.0")).Labels!).Text);
        Assert.Equal("I1", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".in.1")).Labels!).Text);
        Assert.Equal("I2", Assert.Single(node.Ports!.Single(p => p.Id.EndsWith(".in.2")).Labels!).Text);
    }

    [Fact]
    public void Gate_WiresAllInputs()
    {
        GatePrimitive gate = new("gate_y_0", "y", GateKind.And, ["a", "b"], 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [], Primitives: [gate]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.a") && e.Targets.Any(t => t.EndsWith(".in.0")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.b") && e.Targets.Any(t => t.EndsWith(".in.1")));
    }

    // ── Arith ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ArithKind.Add)]
    [InlineData(ArithKind.Sub)]
    [InlineData(ArithKind.Equal)]
    [InlineData(ArithKind.LessThan)]
    public void Arith_EmitsArithNode_WithABYPorts(ArithKind kind)
    {
        ArithPrimitive arith = new("arith_y_0", "y", kind, "a", "b", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [], Primitives: [arith]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsArith(n.Id));
        Assert.Equal(3, node.Ports!.Count);
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".l") &&
            p.Labels![0].Text == "A");
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".r") &&
            p.Labels![0].Text == "B");
        Assert.Contains(node.Ports!, p => p.Id.EndsWith(".out") &&
            p.Labels![0].Text == "Y");
        Assert.StartsWith(kind.ToString(), node.Labels![0].Text);
    }

    [Fact]
    public void Arith_WiresLeftAndRight()
    {
        ArithPrimitive arith = new("arith_y_0", "y", ArithKind.Add, "a", "b", 8);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [], Primitives: [arith]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.a") && e.Targets.Any(t => t.EndsWith(".l")));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.b") && e.Targets.Any(t => t.EndsWith(".r")));
    }

    // ── Legacy-path suppression (Gate / Arith) ────────────────────────────

    [Fact]
    public void GatePrimitive_SuppressesLegacyOperatorNodeForSameTarget()
    {
        // A multi-source contassign for "y" would normally produce an "op_y" node.
        // When a GatePrimitive owns "y", the legacy op_y must be suppressed (no
        // duplicate render).
        GatePrimitive gate = new("gate_y_0", "y", GateKind.And, ["a", "b"], 8);
        DesignContAssign legacyAssign = new("y", ["a", "b"], "&");  // same target

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [legacyAssign], Primitives: [gate]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsGate(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id));
    }

    [Fact]
    public void ArithPrimitive_SuppressesLegacyOperatorNodeForSameTarget()
    {
        ArithPrimitive arith = new("arith_sum_0", "sum", ArithKind.Add, "a", "b", 8);
        DesignContAssign legacyAssign = new("sum", ["a", "b"], "+");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [legacyAssign], Primitives: [arith]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsArith(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id));
    }

    [Fact]
    public void BufferPrimitive_DoesNotSuppressLegacy_BecauseLegacyDoesntRenderSingleSource()
    {
        // Single-source contassigns are never rendered by the legacy operator path
        // (which requires SourceNames.Count >= 2). So Buffer adds a node without any
        // conflict to suppress.
        BufferPrimitive buf = new("buf_y_0", "y", "a", 8);
        DesignContAssign legacyAlias = new("y", ["a"]);  // single source — legacy ignores it

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a")], [], [], [legacyAlias], Primitives: [buf]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsBuffer(n.Id));
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id));
    }

    [Fact]
    public void NonOwnedLegacyContAssigns_StillRenderAsOperatorNodes()
    {
        // If a primitive covers "y" but NOT "z", the legacy contassign for "z" must
        // still produce an operator node (the suppression is per-target).
        GatePrimitive gate = new("gate_y_0", "y", GateKind.And, ["a", "b"], 8);
        DesignContAssign zAssign = new("z", ["a", "b"], "+");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("a"), In("b")], [], [], [zAssign], Primitives: [gate]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsGate(n.Id));
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id) && n.Id == "op_z");
    }

    // ── Discriminator disjointness (extended) ────────────────────────────

    [Theory]
    [InlineData("buf_y",   true, false, false, false)]
    [InlineData("inv_y",   false, true, false, false)]
    [InlineData("gate_y",  false, false, true, false)]
    [InlineData("arith_y", false, false, false, true)]
    [InlineData("op_y",    false, false, false, false)]
    [InlineData("ff_y",    false, false, false, false)]
    [InlineData("mux_y",   false, false, false, false)]
    public void NewIdPrefixes_AreDisjointFromExistingFamilies(
        string id, bool isBuffer, bool isInverter, bool isGate, bool isArith)
    {
        Assert.Equal(isBuffer,   ElkNodeIds.IsBuffer(id));
        Assert.Equal(isInverter, ElkNodeIds.IsInverter(id));
        Assert.Equal(isGate,     ElkNodeIds.IsGate(id));
        Assert.Equal(isArith,    ElkNodeIds.IsArith(id));
    }
}
