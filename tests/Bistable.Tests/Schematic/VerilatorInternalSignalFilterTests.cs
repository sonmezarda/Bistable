using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Schematic;

/// <summary>
/// Phase 2.5 P2.5-4 (level 1): Verilator's DFG-based common sub-expression
/// elimination emits auto-named tmp signals like <c>__VdfgTmp_h1814ef32__0</c>
/// or <c>__Vlvbound_h1234__1</c>. These are compiler-internal optimisation
/// artifacts — never user-meaningful — and produce unreadable operator nodes
/// in the schematic (often with empty operand pins because the other operand
/// is a constant or another tmp).
///
/// Level-1 fix HIDES them from the rendered output. Level-3 fix (P2.6-1)
/// FOLDS the tmp's expression back into its consumer.
///
/// These tests cover both the decoder layer (primitive emission) and the
/// builder layer (legacy contassign nodes).
/// </summary>
public sealed class VerilatorInternalSignalFilterTests
{
    private static ModuleAst MakeModule(
        IReadOnlyList<SignalDecl>? locals = null,
        IReadOnlyList<ContAssignAst>? contAssigns = null,
        IReadOnlyList<SequentialBlockAst>? sequential = null) =>
        new(
            Name: "top",
            IsTop: true,
            Ports: [],
            Parameters: [],
            LocalSignals: locals ?? [],
            Instances: [],
            ContAssigns: contAssigns ?? [],
            SequentialBlocks: sequential ?? [],
            CombinationalBlocks: []);

    // ── Discriminator ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("__VdfgTmp_h1814ef32__0",  true)]
    [InlineData("__Vlvbound_h1234__1",     true)]
    [InlineData("__Vfunc_count_init",      true)]
    [InlineData("__V",                     true)]
    [InlineData("acc_q",                   false)]
    [InlineData("instruction",             false)]
    [InlineData("",                        false)]
    [InlineData("_user_signal",            false)]   // single underscore is user code
    [InlineData("__not_v_prefix",          false)]   // not "__V"
    public void IsVerilatorInternalSignal_MatchesOnlyDoubleVPrefix(string name, bool expected)
    {
        Assert.Equal(expected, SchematicDecoder.IsVerilatorInternalSignal(name));
    }

    // ── Decoder layer: SignalPrimitive ────────────────────────────────────

    [Fact]
    public void VerilatorInternalSignal_NotEmittedAsSignalPrimitive()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(locals:
        [
            new SignalDecl("user_sig", 8, false, []),
            new SignalDecl("__VdfgTmp_h1234__0", 8, false, []),
        ]));

        Assert.Single(result.Signals, s => s.Name == "user_sig");
        Assert.DoesNotContain(result.Signals, s => s.Name.StartsWith("__V"));
    }

    // ── Decoder layer: contassign primitives ──────────────────────────────

    [Fact]
    public void VerilatorInternalTarget_NotEmittedAsBufferPrimitive()
    {
        // assign __VdfgTmp_xx = user_signal;
        ContAssignAst tmpAssign = new(
            new VarRefLValue("__VdfgTmp_xx"),
            new SignalRef("user_signal"));
        ContAssignAst userAssign = new(
            new VarRefLValue("real_target"),
            new SignalRef("source"));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(contAssigns: [tmpAssign, userAssign]));

        Assert.Single(result.Logic.OfType<BufferPrimitive>(), b => b.OutputSignal == "real_target");
        Assert.DoesNotContain(result.Logic.OfType<BufferPrimitive>(), b => b.OutputSignal.StartsWith("__V"));
    }

    [Fact]
    public void VerilatorInternalTarget_NotEmittedAsGatePrimitive()
    {
        ContAssignAst tmpGate = new(
            new VarRefLValue("__VdfgTmp_eq"),
            new BinaryExpr(BinaryOp.And, new SignalRef("a"), new SignalRef("b")));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(contAssigns: [tmpGate]));

        Assert.Empty(result.Logic.OfType<GatePrimitive>());
    }

    // ── Decoder layer: sequential block ───────────────────────────────────

    [Fact]
    public void VerilatorInternalQ_NotEmittedAsFlipFlop()
    {
        // always @(posedge clk) __Vtmp_q <= d;
        SequentialBlockAst tmpBlock = new(
            Triggers: [new EdgeTrigger(EdgeKind.Rising, "clk")],
            Body: new AssignAst(
                new VarRefLValue("__VtmpReg_q"),
                new SignalRef("d"),
                IsNonBlocking: true),
            HasAsynchronousReset: false);

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(sequential: [tmpBlock]));

        Assert.Empty(result.Logic.OfType<FlipFlopPrimitive>());
    }

    // ── Builder layer: legacy operator nodes ──────────────────────────────

    [Fact]
    public void VerilatorInternalContAssign_NotRenderedAsLegacyOperatorNode()
    {
        DesignContAssign tmpAssign = new("__VdfgTmp_xx", ["a", "b"], "+");
        DesignContAssign userAssign = new("real_target", ["a", "b"], "+");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts:
                [
                    new("a", SignalDirection.Input, 8, false),
                    new("b", SignalDirection.Input, 8, false),
                    new("real_target", SignalDirection.Output, 8, false),
                ],
                ChildScopes: [],
                LocalSignals: [],
                ContAssigns: [tmpAssign, userAssign]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsOperator(n.Id) && n.Id == "op_real_target");
        Assert.DoesNotContain(result.Graph.Children, n => n.Id == "op___VdfgTmp_xx");
    }

    // ── Builder layer: legacy splitter nodes ──────────────────────────────

    [Fact]
    public void VerilatorInternalSplitter_NotRenderedAsLegacySplitter()
    {
        // assign __VdfgTmp_slice = bus[3:0];  → should not produce a split_ node
        DesignContAssign tmpSlice = new("__VdfgTmp_slice", ["bus"], null, new DesignBitRange(3, 0));
        DesignContAssign userSlice = new("user_slice", ["bus"], null, new DesignBitRange(7, 4));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [new("bus", SignalDirection.Input, 8, false)],
                ChildScopes: [], LocalSignals: [],
                ContAssigns: [tmpSlice, userSlice]),
            compactLayout: true);

        Assert.Single(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id));
    }

    [Fact]
    public void VerilatorInternalSlice_OnInternalSource_AlsoFiltered()
    {
        // assign user_target = __VtmpBus[3:0]; → also dropped (source is internal)
        DesignContAssign sliceFromTmp = new("user_target", ["__VtmpBus"], null, new DesignBitRange(3, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [], LocalSignals: [],
                ContAssigns: [sliceFromTmp]),
            compactLayout: true);

        Assert.DoesNotContain(result.Graph.Children, n => ElkNodeIds.IsSplitter(n.Id));
    }

    // ── Regression: non-internal signals still work ────────────────────────

    [Fact]
    public void UserSignals_PassThroughUnaffected()
    {
        // Defensive: filter must be precise — user signals with underscores in
        // their names must NOT match.
        var locals = new SignalDecl[]
        {
            new("_my_signal", 8, false, []),
            new("acc_q", 8, false, []),
            new("__user_double_underscore", 8, false, []),   // not __V → user
        };

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(locals: locals));

        Assert.Equal(3, result.Signals.Count);
    }
}
