using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-11: when a <see cref="StructFanOutPrimitive"/> is in the scope, the
/// builder emits a single fan-out ELK node (one west input + N east legs) and
/// routes each leg to its declared consumers. Tests cover happy paths, instance
/// pin consumers, multi-field fan-out, edge wiring (input + legs), label placement,
/// and the discriminator disjointness with other primitive IDs.
/// </summary>
public sealed class ElkGraphBuilderStructFanOutTests
{
    private static HierarchyScopePortViewModel In(string name, int width = 1) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopeInstanceViewModel ChildInst(
        string hierarchyPath, string instanceName, string moduleName,
        params HierarchyScopeInstancePortConnectionViewModel[] ports) =>
        new(hierarchyPath, instanceName, moduleName,
            inputCount: ports.Count(p => p.IsInput),
            outputCount: ports.Count(p => p.IsOutput),
            exactSignalCount: 0, descendantSignalCount: 0,
            ports);

    private static StructFanOutPrimitive MakeFanOut(string structSignal, params (string Field, BitRange Range, string[] Consumers)[] legs) =>
        new(
            Id: $"fanout_{structSignal}_0",
            StructSignal: structSignal,
            StructTypeName: "pkg::s_t",
            StructWidth: legs.Sum(l => l.Range.Width),
            Legs: legs.Select(l => new StructFanOutLeg(l.Field, l.Range, l.Consumers)).ToList());

    // ── Happy path ────────────────────────────────────────────────────────

    [Fact]
    public void FanOut_EmitsNodeWithOneInputAndNLegs()
    {
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("ops",  new BitRange(2, 1), ["t_ops"]),
            ("we",   new BitRange(0, 0), ["t_we"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl", 3)], [], [], [], Primitives: [fanOut]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
        Assert.Equal("fanout_ctrl", node.Id);
        Assert.NotNull(node.Ports);
        // 1 input + 2 legs = 3 ports
        Assert.Equal(3, node.Ports!.Count);
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".in"));
        Assert.Equal(2, node.Ports.Count(p => p.Id.Contains(".leg.")));
    }

    [Fact]
    public void FanOut_InputPort_LabelledWithStructName()
    {
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("ops", new BitRange(0, 0), ["t"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [], [], [], Primitives: [fanOut]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
        ElkPort inPort = node.Ports!.Single(p => p.Id.EndsWith(".in"));
        Assert.Equal("ctrl", Assert.Single(inPort.Labels!).Text);
    }

    [Fact]
    public void FanOut_LegPort_LabelledWithFieldName_WhenSingleBit()
    {
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["t"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [fanOut]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
        ElkPort leg = node.Ports!.Single(p => p.Id.Contains(".leg."));
        Assert.Equal("we", Assert.Single(leg.Labels!).Text);   // no range suffix for 1-bit
    }

    [Fact]
    public void FanOut_LegPort_LabelledWithFieldAndRange_WhenMultiBit()
    {
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("ops", new BitRange(3, 1), ["t"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [fanOut]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
        ElkPort leg = node.Ports!.Single(p => p.Id.Contains(".leg."));
        Assert.Equal("ops[3:1]", Assert.Single(leg.Labels!).Text);
    }

    // ── Edge wiring ───────────────────────────────────────────────────────

    [Fact]
    public void FanOut_InputPort_WiredFromBoundaryInput()
    {
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["t"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [], [], [], Primitives: [fanOut]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("boundary_in.ctrl") &&
            e.Targets.Any(t => t.EndsWith(".in")));
    }

    [Fact]
    public void FanOut_LegPort_DrivesInstancePinConsumer()
    {
        HierarchyScopeInstanceViewModel alu = ChildInst(
            "top.alu_i", "alu_i", "alu",
            new HierarchyScopeInstancePortConnectionViewModel("ops", "?", isInput: true, width: 2));

        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("ops", new BitRange(2, 1), ["alu_i.ops"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl", 3)], [alu], [], [], Primitives: [fanOut]),
            compactLayout: true);

        // Edge: fanout_ctrl.leg.0  →  child_top_alu_i.in.ops
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("fanout_ctrl") && s.Contains(".leg.")) &&
            e.Targets.Contains("child_top_alu_i.in.ops"));
    }

    [Fact]
    public void FanOut_MultipleConsumersOnSameLeg_AllReceiveEdge()
    {
        // One leg, two instance-pin consumers
        HierarchyScopeInstanceViewModel a = ChildInst("top.a", "a", "leaf",
            new HierarchyScopeInstancePortConnectionViewModel("en", "?", isInput: true, width: 1));
        HierarchyScopeInstanceViewModel b = ChildInst("top.b", "b", "leaf",
            new HierarchyScopeInstancePortConnectionViewModel("en", "?", isInput: true, width: 1));

        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["a.en", "b.en"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [a, b], [], [], Primitives: [fanOut]),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("fanout_ctrl")) &&
            e.Targets.Contains("child_top_a.in.en"));
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.StartsWith("fanout_ctrl")) &&
            e.Targets.Contains("child_top_b.in.en"));
    }

    [Fact]
    public void FanOut_OnlyOneEdgeFromBoundary_NotNPerConsumer()
    {
        // The whole point: boundary_in.ctrl produces exactly ONE edge (to the fan-out
        // input), not N edges (one per downstream consumer). Verifies the visual fix.
        HierarchyScopeInstanceViewModel a = ChildInst("top.a", "a", "leaf",
            new HierarchyScopeInstancePortConnectionViewModel("en", "?", isInput: true, width: 1));
        HierarchyScopeInstanceViewModel b = ChildInst("top.b", "b", "leaf",
            new HierarchyScopeInstancePortConnectionViewModel("en", "?", isInput: true, width: 1));

        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["a.en", "b.en"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [a, b], [], [], Primitives: [fanOut]),
            compactLayout: true);

        int edgesFromBoundary = result.Graph.Edges.Count(e => e.Sources.Contains("boundary_in.ctrl"));
        Assert.Equal(1, edgesFromBoundary);
    }

    // ── Discriminator disjointness ────────────────────────────────────────

    [Theory]
    [InlineData("fanout_ctrl",  true,  false, false, false)]
    [InlineData("ff_q",         false, false, false, true)]
    [InlineData("mux_y",        false, false, true,  false)]
    [InlineData("buf_y",        false, true,  false, false)]
    [InlineData("op_y",         false, false, false, false)]
    public void StructFanOutPrefix_DisjointFromOtherPrimitiveIds(
        string id, bool isFanOut, bool isBuffer, bool isMux, bool isFF)
    {
        Assert.Equal(isFanOut, ElkNodeIds.IsStructFanOut(id));
        Assert.Equal(isBuffer, ElkNodeIds.IsBuffer(id));
        Assert.Equal(isMux,    ElkNodeIds.IsMux(id));
        Assert.Equal(isFF,     ElkNodeIds.IsFlipFlop(id));
    }

    // ── Bug regression: fan-out leg + raw struct edge double-bonding ─────

    [Fact]
    public void FanOut_InstancePinConsumer_ReceivesExactlyOneEdge_NoRawStructPath()
    {
        // Bug: previously, when an instance pin's SignalName was the struct base
        // (because the AST reader recovers the base varref name from <sel>), the
        // legacy CollectChildEndpoints registered the pin as a consumer of the
        // unsliced struct. The fan-out then ADDED a synthetic-key consumer, so
        // EmitEdges drew two edges to the same pin:
        //   (a) boundary_in.ctrl → instance.pin   (raw struct path — UNWANTED)
        //   (b) fanout.leg → instance.pin         (correct field path)
        // This test reproduces the SignalName="ctrl" case and asserts only one
        // edge survives.
        HierarchyScopeInstanceViewModel jumpDecoder = ChildInst(
            "top.jd", "jd", "jump_decoder",
            new HierarchyScopeInstancePortConnectionViewModel("jgt", "ctrl", isInput: true, width: 1));

        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("jgt", new BitRange(0, 0), ["jd.jgt"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [jumpDecoder], [], [], Primitives: [fanOut]),
            compactLayout: true);

        // Edges landing on jd.in.jgt — must be exactly ONE, and it must come from
        // the fan-out leg (not the raw boundary_in.ctrl).
        var jgtEdges = result.Graph.Edges
            .Where(e => e.Targets.Contains("child_top_jd.in.jgt"))
            .ToList();
        ElkEdge singleEdge = Assert.Single(jgtEdges);
        Assert.Contains(singleEdge.Sources, s => s.StartsWith("fanout_ctrl"));
        Assert.DoesNotContain(singleEdge.Sources, s => s == "boundary_in.ctrl");
    }

    [Fact]
    public void FanOut_StructSourcedSplitter_IsSuppressed_NoDuplicateNode()
    {
        // Bug: a legacy `assign target = ctrl[i:j];` contassign (still present in
        // scope.ContAssigns) creates a SplitterPrimitive node "split_ctrl" with N
        // output ports — visually competing with the fan-out wedge. When a fan-out
        // owns the struct, the legacy splitter must be suppressed.
        StructFanOutPrimitive fanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["alias_we"]));
        DesignContAssign legacyAlias = new(
            TargetName: "alias_we",
            SourceNames: ["ctrl"],
            OperatorSymbol: null,
            SourceRange: new DesignBitRange(0, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([In("ctrl")], [], [], [legacyAlias], Primitives: [fanOut]),
            compactLayout: true);

        // The split_ctrl node must NOT exist — fan-out owns the struct.
        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id));
        // The fan-out node IS present.
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
    }

    [Fact]
    public void NonFanOutSplitter_StillRendered_WhenSourceIsNotStruct()
    {
        // Defensive: a splitter on a plain (non-struct) bus must still render.
        // The suppression must be precise — only struct-owned splitters get killed.
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Input, 4, false);
        HierarchyScopePortViewModel slice = new("slice", SignalDirection.Output, 2, false);
        DesignContAssign sliceAssign = new("slice", ["bus"], null, new DesignBitRange(1, 0));

        // Include an unrelated struct fan-out (not on "bus")
        StructFanOutPrimitive unrelatedFanOut = MakeFanOut("ctrl",
            ("we", new BitRange(0, 0), ["x"]));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([bus, slice, In("ctrl")], [], [], [sliceAssign], Primitives: [unrelatedFanOut]),
            compactLayout: true);

        // The "bus" splitter still exists; the "ctrl" struct is fan-out only.
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id) && n.Id == "split_bus");
    }

    // ── Regression: no fan-out → no behaviour change ──────────────────────

    [Fact]
    public void NoFanOut_LegacyContAssignBehaviorUnchanged()
    {
        // Plain bus splitter (no struct fan-out involved) still works
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Input, 4, false);
        HierarchyScopePortViewModel slice = new("slice", SignalDirection.Output, 2, false);
        DesignContAssign sliceAssign = new("slice", ["bus"], null, new DesignBitRange(1, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([bus, slice], [], [], [sliceAssign]),
            compactLayout: true);

        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsStructFanOut(n.Id));
        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id));
    }
}
