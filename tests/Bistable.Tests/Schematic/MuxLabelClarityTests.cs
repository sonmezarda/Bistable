using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Schematic;

/// <summary>
/// Phase 2.5 P2.5-6: decoder produces semantic mux labels.
///
/// <para><b>Input-label semantics:</b></para>
/// <list type="bullet">
///   <item>2-input mux (single selector): "1" for the IfTrue branch, "0" for the
///         IfFalse — classic ternary readability.</item>
///   <item>N-input mux (chained ternaries): each branch labelled with the
///         SELECTOR signal that gates it; final default branch labelled "else".
///         Communicates priority-encoder semantics without lying about a
///         non-existent multi-bit selector.</item>
/// </list>
///
/// <para><b>Nested source handling:</b> Complex sub-expressions that can be
/// decoded structurally (e.g. <c>a &amp; b</c>, <c>{c, d}</c>) are materialized
/// as intermediate primitives and feed the mux through synthetic wires. Truly
/// unsupported expressions still become <see cref="MuxConstantSource"/> with
/// literal "X" so the port is intentionally labelled instead of floating.</para>
/// </summary>
public sealed class MuxLabelClarityTests
{
    private static ModuleAst Wrap(ContAssignAst ca) => new(
        Name: "top", IsTop: true,
        Ports: [], Parameters: [], LocalSignals: [], Instances: [],
        ContAssigns: [ca],
        SequentialBlocks: [], CombinationalBlocks: []);

    // ── Input labels: simple ternary preserves "1"/"0" ────────────────────

    /// <summary>SV equivalent: <c>assign y = sel ? a : b;</c></summary>
    [Fact]
    public void SimpleTernary_InputLabels_Are1And0()
    {
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(new SignalRef("sel"), new SignalRef("a"), new SignalRef("b")));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        Assert.Equal(2, mux.Inputs.Count);
        Assert.Equal("1", mux.Inputs[0].Label);
        Assert.Equal("0", mux.Inputs[1].Label);
    }

    // ── Input labels: chained ternary uses selector names ─────────────────

    /// <summary>SV equivalent: <c>assign y = s2 ? a : s1 ? b : s0 ? c : d;</c></summary>
    [Fact]
    public void ChainedTernary_InputLabels_AreSelectorNamesPlusElse()
    {
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("s2"), new SignalRef("a"),
                new CondExpr(
                    new SignalRef("s1"), new SignalRef("b"),
                    new CondExpr(
                        new SignalRef("s0"), new SignalRef("c"),
                        new SignalRef("d")))));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());

        Assert.Equal(4, mux.Inputs.Count);
        Assert.Equal("s2",   mux.Inputs[0].Label);
        Assert.Equal("s1",   mux.Inputs[1].Label);
        Assert.Equal("s0",   mux.Inputs[2].Label);
        Assert.Equal("else", mux.Inputs[3].Label);

        // Sources still point to the right wires
        Assert.Equal("a", ((MuxSignalSource)mux.Inputs[0].Source).SignalName);
        Assert.Equal("b", ((MuxSignalSource)mux.Inputs[1].Source).SignalName);
        Assert.Equal("c", ((MuxSignalSource)mux.Inputs[2].Source).SignalName);
        Assert.Equal("d", ((MuxSignalSource)mux.Inputs[3].Source).SignalName);
    }

    [Fact]
    public void ChainedTernary_SelectSignals_ListedInOrder()
    {
        // The chain order: outer selector first, innermost last
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("outer"), new SignalRef("a"),
                new CondExpr(new SignalRef("inner"), new SignalRef("b"), new SignalRef("c"))));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        Assert.Equal(new[] { "outer", "inner" }, mux.SelectSignals);
    }

    // ── Orphan source: complex sub-expression → constant X ────────────────

    [Fact]
    public void NestedInput_ComplexExpression_MaterializesGateSource()
    {
        // assign y = sel ? (a & b) : c;  — the (a & b) is a BinaryExpr, so the
        // decoder must materialize it as a real gate feeding the mux input.
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new BinaryExpr(BinaryOp.And, new SignalRef("a"), new SignalRef("b")),
                new SignalRef("c")));

        SchematicPrimitiveList decoded = SchematicDecoder.Decode(Wrap(ca));
        MuxPrimitive mux = Assert.Single(decoded.Logic.OfType<MuxPrimitive>());
        Assert.Equal(2, mux.Inputs.Count);

        MuxSignalSource source = Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);
        GatePrimitive gate = Assert.Single(decoded.Logic.OfType<GatePrimitive>());
        Assert.Equal(source.SignalName, gate.OutputSignal);
        Assert.Equal(GateKind.And, gate.Kind);
        Assert.Equal(new[] { "a", "b" }, gate.InputSignals);

        // Second input was a plain signal — unaffected
        Assert.IsType<MuxSignalSource>(mux.Inputs[1].Source);
    }

    [Fact]
    public void MaterializedInput_LabelKeepsBranchMeaning()
    {
        // Once the complex branch has a real wire, the label should stay focused
        // on the branch condition instead of showing the older X suffix.
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new BinaryExpr(BinaryOp.And, new SignalRef("a"), new SignalRef("b")),
                new SignalRef("c")));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());

        Assert.Equal("1", mux.Inputs[0].Label);
        Assert.Equal("0", mux.Inputs[1].Label);
    }

    [Fact]
    public void MaterializedInput_LabelKeepsBranchMeaning_InChainedTernary()
    {
        // Chained: s1 ? (a & b) : s0 ? c : d
        // Selector labels: s1, s0; "else" for final default.
        // First branch source is materialized → label remains "s1".
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("s1"),
                new BinaryExpr(BinaryOp.And, new SignalRef("a"), new SignalRef("b")),
                new CondExpr(new SignalRef("s0"), new SignalRef("c"), new SignalRef("d"))));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        Assert.Equal("s1", mux.Inputs[0].Label);
        Assert.Equal("s0",   mux.Inputs[1].Label);
        Assert.Equal("else", mux.Inputs[2].Label);
    }

    /// <summary>
    /// SV equivalent: <c>assign y = ctrl[2] ? a : ctrl[1] ? b : ctrl[0] ? c : d;</c>.
    /// <para>
    /// Contract: <see cref="MuxPrimitive.SelectSignals"/> holds BARE wire-up names
    /// ("ctrl") so the builder's endpoint registration matches a real producer.
    /// <see cref="MuxPrimitive.SelectorLabels"/> holds the human-readable variants
    /// ("ctrl[2]" / "ctrl[1]" / "ctrl[0]") for port glyphs. Input labels mirror the
    /// display labels so the branch each input feeds is identifiable at a glance.
    /// </para>
    /// </summary>
    [Fact]
    public void ChainedTernaryOnBitSelects_PreservesWireNameWhileShowingBitRange()
    {
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new BitSelectExpr(new SignalRef("ctrl"), new BitRange(2, 2)),
                new SignalRef("a"),
                new CondExpr(
                    new BitSelectExpr(new SignalRef("ctrl"), new BitRange(1, 1)),
                    new SignalRef("b"),
                    new CondExpr(
                        new BitSelectExpr(new SignalRef("ctrl"), new BitRange(0, 0)),
                        new SignalRef("c"),
                        new SignalRef("d")))));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());

        // SelectSignals = wire-up names (all "ctrl" — the bare parent signal)
        Assert.Equal(new[] { "ctrl", "ctrl", "ctrl" }, mux.SelectSignals);
        // SelectorLabels = display variants (bit-aware)
        Assert.NotNull(mux.SelectorLabels);
        Assert.Equal(new[] { "ctrl[2]", "ctrl[1]", "ctrl[0]" }, mux.SelectorLabels!);
        // Input labels echo the display labels so branches are identifiable
        Assert.Equal(new[] { "ctrl[2]", "ctrl[1]", "ctrl[0]", "else" },
            mux.Inputs.Select(i => i.Label).ToArray());
    }

    /// <summary>
    /// Builder must use SelectorLabels for the port glyph, NOT SelectSignals.
    /// This is the visual half of the wire-name vs display-label separation.
    /// </summary>
    [Fact]
    public void Builder_UsesSelectorLabel_ForPortGlyph_WhenAvailable()
    {
        MuxPrimitive mux = new(
            "mux_y_0", "y",
            SelectSignals: ["ctrl", "ctrl"],          // bare wire-up names
            Inputs: [
                new("ctrl[2]", new MuxSignalSource("a")),
                new("ctrl[1]", new MuxSignalSource("b")),
                new("else",    new MuxSignalSource("c")),
            ],
            Width: 8,
            SelectorLabels: ["ctrl[2]", "ctrl[1]"]);  // display variants

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [new HierarchyScopePortViewModel("a", SignalDirection.Input, 1, false),
                                new HierarchyScopePortViewModel("b", SignalDirection.Input, 1, false),
                                new HierarchyScopePortViewModel("c", SignalDirection.Input, 1, false),
                                new HierarchyScopePortViewModel("ctrl", SignalDirection.Input, 4, false)],
                ChildScopes: [], LocalSignals: [], ContAssigns: [],
                Primitives: [mux]),
            compactLayout: true);

        var node = result.Graph.Children.Single(n => ElkNodeIds.IsMux(n.Id));
        var sel0 = node.Ports!.Single(p => p.Id.EndsWith(".sel.0"));
        var sel1 = node.Ports!.Single(p => p.Id.EndsWith(".sel.1"));
        Assert.Equal("ctrl[2]", sel0.Labels![0].Text);
        Assert.Equal("ctrl[1]", sel1.Labels![0].Text);
    }

    [Fact]
    public void ConstantSource_LabelShowsConstantValueSuffix()
    {
        // Constants have no incoming wire by design — to keep the visual clear,
        // we suffix the branch label with the literal value. So a `sel ? a : 8'h0`
        // yields "1" for the signal branch, "0·0" for the constant branch (branch
        // label "0", suffix "·0" = the literal). Don't-care X uses the same
        // suffix mechanism, just with "X" as the literal.
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new SignalRef("a"),
                new ConstExpr(System.Numerics.BigInteger.Zero, 8, false)));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        Assert.Equal("1", mux.Inputs[0].Label);     // signal branch — no suffix
        Assert.Equal("0·0", mux.Inputs[1].Label);   // constant branch — suffix with value
    }

    [Fact]
    public void UnconnectedMuxPort_AlwaysHasLabelSuffix_NeverAmbiguous()
    {
        // Contract: every mux input port with no incoming wire MUST carry a label
        // suffix ("·X" for orphan/internal-tmp, "·<value>" for constants) so the
        // user can tell at a glance that the empty port is intentional, not a bug.
        // Tested cases:
        //   1. unmaterializable source → "1·X"
        //   2. internal-tmp source     → label of original SOURCE replaced with "·X"
        //   3. literal constant        → "0·<value>"
        ContAssignAst unsupported = new(
            new VarRefLValue("y1"),
            new CondExpr(new SignalRef("s"),
                new FunctionCallExpr("user_func", [new SignalRef("a")]),
                new SignalRef("c")));
        ContAssignAst tmp = new(
            new VarRefLValue("y2"),
            new CondExpr(new SignalRef("s"),
                new SignalRef("__VdfgTmp_h1234__0"),   // internal tmp — should fold to X
                new SignalRef("c")));
        ContAssignAst constAst = new(
            new VarRefLValue("y3"),
            new CondExpr(new SignalRef("s"),
                new SignalRef("a"),
                new ConstExpr(System.Numerics.BigInteger.One, 1, false)));

        ModuleAst module = new(
            Name: "top", IsTop: true,
            Ports: [], Parameters: [], LocalSignals: [], Instances: [],
            ContAssigns: [unsupported, tmp, constAst],
            SequentialBlocks: [], CombinationalBlocks: []);

        var muxes = SchematicDecoder.Decode(module).Logic.OfType<MuxPrimitive>().ToList();
        Assert.Equal(3, muxes.Count);

        Assert.Equal("1·X", muxes[0].Inputs[0].Label);   // unsupported expression
        Assert.Equal("1·X", muxes[1].Inputs[0].Label);   // tmp source
        Assert.Equal("0·1", muxes[2].Inputs[1].Label);   // constant
    }

    /// <summary>SV equivalent: <c>assign y = sel ? {a, b} : c;</c></summary>
    [Fact]
    public void NestedInput_ConcatExpression_MaterializesJoinerSource()
    {
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new ConcatExpr([new SignalRef("a"), new SignalRef("b")]),
                new SignalRef("c")));

        SchematicPrimitiveList decoded = SchematicDecoder.Decode(Wrap(ca));
        MuxPrimitive mux = Assert.Single(decoded.Logic.OfType<MuxPrimitive>());
        MuxSignalSource source = Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);
        JoinerPrimitive joiner = Assert.Single(decoded.Logic.OfType<JoinerPrimitive>());
        Assert.Equal(source.SignalName, joiner.OutputSignal);
        Assert.Equal(new[] { "a", "b" }, joiner.InputSignals);
    }

    [Fact]
    public void NoOrphan_BitSelectExpression_BecomesSignalSource()
    {
        // Regression: BitSelectExpr DOES reduce (via ExpressionToSignalName) — must NOT
        // become a don't-care.
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new BitSelectExpr(new SignalRef("bus"), new BitRange(3, 0)),
                new SignalRef("c")));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        MuxSignalSource sig = Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);
        Assert.Equal("bus", sig.SignalName);
    }

    // ── Regression: existing constant-branch case unaffected ───────────────

    [Fact]
    public void ConstantBranch_StillEmitsMuxConstantSource_WithLiteralValue()
    {
        // assign y = sel ? a : 8'h0;  — constant branch should still come through
        // unchanged (don't accidentally relabel real constants as "X").
        ContAssignAst ca = new(
            new VarRefLValue("y"),
            new CondExpr(
                new SignalRef("sel"),
                new SignalRef("a"),
                new ConstExpr(System.Numerics.BigInteger.Zero, 8, false)));

        MuxPrimitive mux = Assert.Single(SchematicDecoder.Decode(Wrap(ca)).Logic.OfType<MuxPrimitive>());
        MuxConstantSource constSrc = Assert.IsType<MuxConstantSource>(mux.Inputs[1].Source);
        Assert.Equal("0", constSrc.Literal);
        Assert.Equal(8, constSrc.Width);
    }

    // ── End-to-end: chained ternary in XML → priority labels ──────────────

    [Fact]
    public void EndToEnd_ChainedTernaryInXml_ProducesSemanticLabels()
    {
        string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1">
                  <var name="s2" dtype_id="1" dir="input" pinIndex="1" vartype="logic"/>
                  <var name="s1" dtype_id="1" dir="input" pinIndex="2" vartype="logic"/>
                  <var name="s0" dtype_id="1" dir="input" pinIndex="3" vartype="logic"/>
                  <var name="a"  dtype_id="8" dir="input" pinIndex="4" vartype="logic"/>
                  <var name="b"  dtype_id="8" dir="input" pinIndex="5" vartype="logic"/>
                  <var name="c"  dtype_id="8" dir="input" pinIndex="6" vartype="logic"/>
                  <var name="d"  dtype_id="8" dir="input" pinIndex="7" vartype="logic"/>
                  <var name="y"  dtype_id="8" dir="output" pinIndex="8" vartype="logic"/>
                  <contassign dtype_id="8">
                    <cond dtype_id="8">
                      <varref name="s2"/>
                      <varref name="a"/>
                      <cond dtype_id="8">
                        <varref name="s1"/>
                        <varref name="b"/>
                        <cond dtype_id="8">
                          <varref name="s0"/>
                          <varref name="c"/>
                          <varref name="d"/>
                        </cond>
                      </cond>
                    </cond>
                    <varref name="y"/>
                  </contassign>
                </module>
              </netlist>
            </verilator_xml>
            """;

        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, xml);
            DesignAst ast = new Bistable.Verilator.VerilatorXmlAstReader().Read(path);
            SchematicPrimitiveList result = SchematicDecoder.Decode(ast.TopModule!);

            MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
            Assert.Equal(new[] { "s2", "s1", "s0" }, mux.SelectSignals);
            Assert.Equal(new[] { "s2", "s1", "s0", "else" },
                mux.Inputs.Select(i => i.Label).ToArray());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
