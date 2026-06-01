using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 2 P2-8: when a compound child is expanded, the renderer descends into the
/// child's MODULE primitives (decoded from the AST and supplied via
/// <see cref="ElkScopeData.PrimitivesByModule"/>) and renders them as children of
/// the compound's ELK node — even when the child has no sub-instances.
///
/// Inner edge wiring (FF.D → compound's "clk" boundary, etc.) is the P2-8b follow-up.
/// These tests therefore assert only the NODE presence + structural contracts:
///   • inner primitive nodes appear as children of the expanded compound
///   • their IDs are scoped with the compound's hierarchy path to avoid collisions
///   • the compound's expand button stays available when only primitives (no
///     sub-instances) exist
///   • the regression: when PrimitivesByModule is null/empty, nothing changes
/// </summary>
public sealed class ElkGraphBuilderRecursiveCompoundTests
{
    private static HierarchyScopeInstanceViewModel Child(string path, string moduleName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports,
        IReadOnlyList<HierarchyScopeInstanceViewModel>? grandchildren = null) =>
        new(path, path.Split('.')[^1], moduleName,
            inputCount: ports.Count(p => p.IsInput),
            outputCount: ports.Count(p => p.IsOutput),
            exactSignalCount: 0, descendantSignalCount: 0,
            ports,
            childInstances: grandchildren);

    private static FlipFlopPrimitive MakeFF(string q) =>
        new($"ff_{q}_0", q, "clk", EdgeKind.Rising, null, null, "d", Width: 8);

    private static HierarchyScopePortViewModel In(string name, int width = 8) =>
        new(name, SignalDirection.Input, width, false);

    private static HierarchyScopePortViewModel Out(string name, int width = 8) =>
        new(name, SignalDirection.Output, width, false);

    private static IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>
        ByModule(string moduleName, params SchematicPrimitive[] primitives) =>
        new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName] = primitives
        };

    // ── Happy path: leaf module with primitives renders inside expanded compound ─

    [Fact]
    public void ExpandedCompound_WithModulePrimitives_AddsPrimitiveNodesInside()
    {
        // top instantiates "leaf" once; leaf has no sub-instances but contains a FF.
        // After expansion, the leaf compound's interior should show the FF node.
        HierarchyScopeInstanceViewModel leafInstance = Child(
            "top.leaf_i", "leaf",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true,  width: 1),
                new HierarchyScopeInstancePortConnectionViewModel("q",   "leaf_q", false, width: 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.leaf_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [leafInstance],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("leaf", MakeFF("q"))),
            compactLayout: true);

        ElkNode leafNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_leaf_i");
        Assert.NotNull(leafNode.Children);
        // P2.5-5: inner FF id keeps the "ff_" prefix at the START so the renderer's
        // IsFlipFlop StartsWith dispatch fires. Scope is encoded as a `__` suffix.
        Assert.Contains(leafNode.Children!, c => ElkNodeIds.IsFlipFlop(c.Id));
        Assert.Contains(leafNode.Children!, c => c.Id == "ff_top_leaf_i__q");
    }

    // ── Expand button surfaces when only inner primitives exist (no sub-instances) ─

    [Fact]
    public void LeafModule_WithPrimitives_IsMarkedExpandable()
    {
        HierarchyScopeInstanceViewModel leafInstance = Child(
            "top.leaf_i", "leaf",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("q", "lq", false, 8)]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [leafInstance],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: null,   // not yet expanded — just checking the marker
                PrimitivesByModule: ByModule("leaf", MakeFF("q"))),
            compactLayout: true);

        ElkNode leafNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_leaf_i");
        Assert.True(ElkGraphBuilder.IsExpandableChild(leafNode));
    }

    // ── Multiple primitives inside a single compound ─────────────────────────

    [Fact]
    public void ExpandedCompound_WithMultiplePrimitives_AllRenderInside()
    {
        HierarchyScopeInstanceViewModel cpu = Child(
            "top.cpu", "cpu",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1)]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cpu" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [cpu],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("cpu",
                    MakeFF("pc"),
                    MakeFF("acc"),
                    new MuxPrimitive("mux_alu_out_0", "alu_out",
                        SelectSignals: ["op"],
                        Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
                        Width: 8))),
            compactLayout: true);

        ElkNode cpuNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_cpu");
        Assert.NotNull(cpuNode.Children);
        Assert.Equal(2, cpuNode.Children!.Count(c => c.Id.Contains("ff_")));
        Assert.Equal(1, cpuNode.Children!.Count(c => c.Id.Contains("mux_")));
    }

    // ── Scoped IDs avoid outer-scope collisions ─────────────────────────────

    [Fact]
    public void InnerPrimitiveId_IsPrefixedByCompoundPath_NoCollisionWithOuter()
    {
        // Outer scope has its own FF for "q" (somehow); inner compound's module ALSO
        // has a FF for "q" with the same name. They must produce distinct ELK IDs.
        HierarchyScopeInstanceViewModel inst = Child(
            "top.sub_i", "sub_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1)]);

        FlipFlopPrimitive outerFF = MakeFF("q");
        FlipFlopPrimitive innerFF = MakeFF("q");   // same signal name, different scope

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.sub_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [In("d"), In("clk", 1), Out("q")],
                ChildScopes: [inst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                Primitives: [outerFF],
                PrimitivesByModule: ByModule("sub_mod", innerFF)),
            compactLayout: true);

        // Outer FF
        Assert.Single(result.Graph.Children, n => n.Id == "ff_q");
        // Inner FF (scoped via `__` suffix to keep the type prefix at the start)
        ElkNode subNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_sub_i");
        Assert.Contains(subNode.Children!, c => c.Id == "ff_top_sub_i__q");
    }

    // ── Regression: no PrimitivesByModule → no behaviour change ─────────────

    [Fact]
    public void NoPrimitivesByModule_LeafModuleWithoutGrandchildren_IsNotExpandable()
    {
        // Without the catalog, a leaf module (no sub-instances) is NOT marked
        // expandable — exactly as it was before P2-8.
        HierarchyScopeInstanceViewModel leaf = Child(
            "top.leaf", "leaf_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 8)]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [leaf],
                LocalSignals: [],
                ContAssigns: []),   // no PrimitivesByModule
            compactLayout: true);

        ElkNode leafNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_leaf");
        Assert.False(ElkGraphBuilder.IsExpandableChild(leafNode));
    }

    [Fact]
    public void PrimitivesByModule_WithMissingEntry_NoInnerNodesRendered()
    {
        // Compound module name not in the catalog — the renderer treats it as
        // having no inner primitives, matching the legacy behaviour.
        HierarchyScopeInstanceViewModel inst = Child(
            "top.mystery", "unknown_module",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 8)]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.mystery" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [inst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("some_other_module", MakeFF("q"))),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => n.Id == "child_top_mystery");
        // No grandchildren and no matching module → no inner nodes
        Assert.True(node.Children is null || node.Children.Count == 0);
    }

    // ── Non-expanded compound: no inner nodes emitted even if module has primitives ─

    [Fact]
    public void NonExpandedCompound_DoesNotEmitInnerPrimitives()
    {
        HierarchyScopeInstanceViewModel leaf = Child(
            "top.leaf", "leaf",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1)]);

        // Compound has primitives in its module but expansion is OFF.
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [leaf],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: null,
                PrimitivesByModule: ByModule("leaf", MakeFF("q"))),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, n => n.Id == "child_top_leaf");
        Assert.True(node.Children is null || node.Children.Count == 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2-8b: inner-scope edge wiring
    // ═══════════════════════════════════════════════════════════════════════

    // Helper: build a standard scope for edge wiring tests.
    // The compound "reg" has ports "d" (in), "clk" (in), "q" (out) connected to
    // outer signals "d_in", "clk", "q_out".  Its module contains a FF (q←d on clk).
    private static ElkBuildResult BuildFFCompoundResult(
        bool expanded,
        FlipFlopPrimitive? ff = null)
    {
        ff ??= new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d", Width: 8);

        HierarchyScopeInstanceViewModel reg = Child(
            "top.reg", "reg_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",   "d_in",  true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk",   true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("q",   "q_out", false, 8),
            ]);

        var expandedPaths = expanded
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.reg" }
            : null;

        return new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [reg],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expandedPaths,
                PrimitivesByModule: ByModule("reg_mod", ff)),
            compactLayout: true);
    }

    [Fact]
    public void InnerFF_D_GetsEdgeFromCompoundBoundaryInput()
    {
        // When expanded, the FF.D port should be wired to the compound's "d" input.
        // The compound port "d" is connected to outer signal "d_in" but inside the
        // module the port name itself is the inner signal name ("d").
        ElkBuildResult result = BuildFFCompoundResult(expanded: true);

        // Edge sources: the compound's input port for "d"
        string compoundInputPortId = "child_top_reg.in.d";
        // Edge target: the inner FF's D port — P2.5-5 uses named-suffix port ids (.d/.clk/.rst/.q)
        // identical to the outer FF, so DrawElkFlipFlopNode's clock-triangle logic still fires.
        string ffDPortId = "ff_top_reg__q.d";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(compoundInputPortId) && e.Targets.Contains(ffDPortId));
    }

    [Fact]
    public void InnerFF_Clk_GetsEdgeFromCompoundBoundaryInput()
    {
        ElkBuildResult result = BuildFFCompoundResult(expanded: true);

        string compoundClkPortId = "child_top_reg.in.clk";
        string ffClkPortId = "ff_top_reg__q.clk";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(compoundClkPortId) && e.Targets.Contains(ffClkPortId));
    }

    [Fact]
    public void InnerFF_Q_GetsEdgeToCompoundBoundaryOutput()
    {
        // The FF.Q output should drive the compound's "q" boundary output.
        ElkBuildResult result = BuildFFCompoundResult(expanded: true);

        string ffQPortId   = "ff_top_reg__q.q";
        string compoundQId = "child_top_reg.out.q";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(ffQPortId) && e.Targets.Contains(compoundQId));
    }

    [Fact]
    public void InnerFF_AsyncReset_GetsEdgeFromCompoundBoundaryInput()
    {
        // Compound "reg_mod" has ports d/clk/rst_n (inputs) and q (output).
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, "rst_n", EdgeKind.Falling, "d", Width: 8);

        HierarchyScopeInstanceViewModel reg = Child(
            "top.reg", "reg_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",     "d_in",   true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk",   "clk",    true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("rst_n", "rst_n",  true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("q",     "q_out",  false, 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.reg" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [reg],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("reg_mod", ff)),
            compactLayout: true);

        // Reset port — named-suffix `.rst` matches outer FF format.
        string ffRstPortId  = "ff_top_reg__q.rst";
        string compoundRstId = "child_top_reg.in.rst_n";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(compoundRstId) && e.Targets.Contains(ffRstPortId));
    }

    [Fact]
    public void InnerMux_Selector_GetsEdgeFromCompoundBoundaryInput()
    {
        // Compound "mux_mod" has selector input "sel" and output "y".
        // Inner mux: output="y", sel=["sel"], inputs=[a,b]
        var mux = new MuxPrimitive("mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);

        HierarchyScopeInstanceViewModel inst = Child(
            "top.mx", "mux_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("a",   "a_in", true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("b",   "b_in", true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("sel", "sel",  true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("y",   "y",    false, 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.mx" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [inst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("mux_mod", mux)),
            compactLayout: true);

        // Mux selectors use the outer `.sel.<i>` format (kept distinct from `.in.<i>` data ports).
        string muxSelPortId    = "mux_top_mx__y.sel.0";
        string compoundSelPort = "child_top_mx.in.sel";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(compoundSelPort) && e.Targets.Contains(muxSelPortId));
    }

    [Fact]
    public void TwoInnerPrimitives_WireToEachOtherViaLocalSignal()
    {
        // Compound has: FF (q←d_in on clk) then Buffer (buf_out←q).
        // The FF.Q and Buffer.in share the inner signal "q" — edge should appear.
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d_in", Width: 8);
        BufferPrimitive buf  = new("buf_out_0", "buf_out", "q", Width: 8);

        HierarchyScopeInstanceViewModel inst = Child(
            "top.pipe", "pipe_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d_in",   "d_in",   true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk",    "clk",    true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("buf_out", "buf_out", false, 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.pipe" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [inst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("pipe_mod", ff, buf)),
            compactLayout: true);

        // FF.Q → Buffer.in: both share inner signal "q"
        string ffQPort  = "ff_top_pipe__q.q";
        string bufInPort = "buf_top_pipe__buf_out.in";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(ffQPort) && e.Targets.Contains(bufInPort));
    }

    [Fact]
    public void InnerPrimitiveSignal_DoesNotLeakToOuterScope()
    {
        // The compound has an inner FF whose signal "q" is purely internal.
        // The outer scope has NO signal named "q" — we assert that the outer graph
        // has no edges whose label text is "q" (scoped key prevents collisions).
        FlipFlopPrimitive ff = MakeFF("q"); // q is purely internal to "reg_mod"

        HierarchyScopeInstanceViewModel reg = Child(
            "top.reg", "reg_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",   "d_in", true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk",  true,  1),
            ]); // NO "q" output port — q is purely internal

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.reg" };

        // Outer scope also has a FF for signal "q" (collision-risk test)
        FlipFlopPrimitive outerFF = MakeFF("q");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [reg],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                Primitives: [outerFF],
                PrimitivesByModule: ByModule("reg_mod", ff)),
            compactLayout: true);

        // The outer FF's Q port ("ff_q.q") should NOT drive any inner-compound port.
        // Inner signal "q" has no consumer in the outer scope either, so no cross-scope
        // edge for "q" should appear at all.
        const string outerFFQPort = "ff_q.q";
        Assert.DoesNotContain(result.Graph.Edges,
            e => e.Sources.Contains(outerFFQPort)
                 && e.Targets.Any(t => t.StartsWith("ff_top_reg__", StringComparison.Ordinal)));

        // Also assert no inner-primitive OUTPUT port drives a purely outer-scope port
        // (e.g. inner FF.Q must not reach the outer FF's D port).
        Assert.DoesNotContain(result.Graph.Edges,
            e => e.Sources.Any(s => s.StartsWith("ff_top_reg__", StringComparison.Ordinal))
                 && e.Targets.Any(t => !t.StartsWith("ff_top_reg__", StringComparison.Ordinal)
                                       && !t.StartsWith("child_top_reg", StringComparison.Ordinal)));
    }

    [Fact]
    public void NestedExpandedCompound_InnermostPrimitive_IsWired()
    {
        // Two levels of expansion: top.outer (expanded) → inner_mod (expanded) → FF inside inner_mod.
        // The compound "outer_mod" has no primitives of its own; its child "inner_i" (module "inner_mod")
        // does have a FF.  We expand both levels.
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d", Width: 8);

        HierarchyScopeInstanceViewModel innerInst = Child(
            "top.outer_i.inner_i", "inner_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",   "d",  true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("q",   "q",  false, 8),
            ]);

        HierarchyScopeInstanceViewModel outerInst = Child(
            "top.outer_i", "outer_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",   "d",  true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("q",   "q",  false, 8),
            ],
            grandchildren: [innerInst]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "top.outer_i",
            "top.outer_i.inner_i"
        };

        // PrimitivesByModule: inner_mod has the FF; outer_mod has none
        var byModule = new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            ["inner_mod"] = [ff]
        };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [outerInst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: byModule),
            compactLayout: true);

        // The innermost FF.D should be wired to the inner compound's "d" boundary input
        string innerCompoundId = "child_top_outer_i_inner_i";
        string ffDPortId = "ff_top_outer_i_inner_i__q.d";
        string innerDPortId = $"{innerCompoundId}.in.d";

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains(innerDPortId) && e.Targets.Contains(ffDPortId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2.5-5: inner primitives reach the proper symbol drawer + port labels +
    //         compound min-size grows with content
    // ═══════════════════════════════════════════════════════════════════════

    // Helper: locate an inner node inside an expanded compound by exact id.
    private static ElkNode InnerNode(ElkBuildResult r, string compoundId, string innerId)
    {
        ElkNode compound = Assert.Single(r.Graph.Children, n => n.Id == compoundId);
        Assert.NotNull(compound.Children);
        return Assert.Single(compound.Children!, c => c.Id == innerId);
    }

    [Fact]
    public void InnerFlipFlop_NodeId_StartsWithFfPrefix_SoDispatchFires()
    {
        // P2.5-5 root cause: the renderer dispatches via IsFlipFlop (StartsWith "ff_").
        // The old `child_<scope>/ff_<sig>` id broke that check; the new `ff_<scope>__<sig>`
        // restores it, ensuring DrawElkFlipFlopNode is reached instead of the generic card.
        ElkBuildResult r = BuildFFCompoundResult(expanded: true);
        ElkNode ff = InnerNode(r, "child_top_reg", "ff_top_reg__q");

        Assert.True(ElkNodeIds.IsFlipFlop(ff.Id), "Inner FF id must satisfy IsFlipFlop StartsWith check");
        Assert.False(ElkNodeIds.IsLatch(ff.Id));
        Assert.False(ElkNodeIds.IsMux(ff.Id));
    }

    [Fact]
    public void InnerMux_NodeId_StartsWithMuxPrefix_SoDispatchFires()
    {
        var mux = new MuxPrimitive("mux_y_0", "y",
            SelectSignals: ["sel"],
            Inputs: [new("1", new MuxSignalSource("a")), new("0", new MuxSignalSource("b"))],
            Width: 8);
        HierarchyScopeInstanceViewModel inst = Child("top.mx", "mux_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("sel", "sel", true, 1)]);
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.mx" };

        ElkBuildResult r = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [], ChildScopes: [inst], LocalSignals: [], ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("mux_mod", mux)),
            compactLayout: true);

        ElkNode muxNode = InnerNode(r, "child_top_mx", "mux_top_mx__y");
        Assert.True(ElkNodeIds.IsMux(muxNode.Id));
    }

    [Fact]
    public void InnerPrimitive_PortLabels_MatchOuterPrimitive()
    {
        // Inner FF must carry the same IEEE-91 pin glyphs (D / > / Q) as the outer FF,
        // because DrawSymbolPortsAndLabels paints port.Labels[0] inside the symbol body.
        ElkBuildResult r = BuildFFCompoundResult(expanded: true);
        ElkNode ff = InnerNode(r, "child_top_reg", "ff_top_reg__q");

        Assert.NotNull(ff.Ports);
        ElkPort dPort   = Assert.Single(ff.Ports!, p => p.Id.EndsWith(".d", StringComparison.Ordinal));
        ElkPort clkPort = Assert.Single(ff.Ports!, p => p.Id.EndsWith(".clk", StringComparison.Ordinal));
        ElkPort qPort   = Assert.Single(ff.Ports!, p => p.Id.EndsWith(".q", StringComparison.Ordinal));

        Assert.Equal("D", dPort.Labels![0].Text);
        Assert.Equal(">", clkPort.Labels![0].Text);   // edge-trigger marker
        Assert.Equal("Q", qPort.Labels![0].Text);
    }

    [Fact]
    public void InnerPrimitive_PortIdSuffix_MatchesOuterFormat_NotGenericIndex()
    {
        // Before P2.5-5 inner ports used a generic `.in.<i>` format which broke the
        // FF symbol drawer's `port.Id.EndsWith(".clk")` logic (no clock triangle).
        // After P2.5-5 they use named suffixes (.d/.clk/.rst/.q) identical to outer.
        ElkBuildResult r = BuildFFCompoundResult(expanded: true);
        ElkNode ff = InnerNode(r, "child_top_reg", "ff_top_reg__q");

        Assert.NotNull(ff.Ports);
        Assert.Contains(ff.Ports!, p => p.Id == "ff_top_reg__q.d");
        Assert.Contains(ff.Ports!, p => p.Id == "ff_top_reg__q.clk");
        Assert.Contains(ff.Ports!, p => p.Id == "ff_top_reg__q.q");
        // No generic `.in.0` form.
        Assert.DoesNotContain(ff.Ports!, p => p.Id.Contains(".in.", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandedCompound_MinimumSize_GrowsWithInnerPrimitiveCount()
    {
        // Two compounds with the same shape but different inner-primitive counts.
        // The one with more primitives should be at least as wide as the one with fewer.
        ElkBuildResult small = BuildFFCompoundResult(expanded: true);
        ElkNode smallCompound = Assert.Single(small.Graph.Children, n => n.Id == "child_top_reg");

        // Compound with 4 inner primitives (FF + 3 buffers chained through "q").
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d", Width: 8);
        BufferPrimitive b1 = new("buf_x1_0", "x1", "q",  Width: 8);
        BufferPrimitive b2 = new("buf_x2_0", "x2", "x1", Width: 8);
        BufferPrimitive b3 = new("buf_x3_0", "x3", "x2", Width: 8);

        HierarchyScopeInstanceViewModel big = Child(
            "top.big", "big_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("d",   "d_in", true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk",  true,  1),
                new HierarchyScopeInstancePortConnectionViewModel("y",   "y",    false, 8),
            ]);
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.big" };

        ElkBuildResult bigResult = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [], ChildScopes: [big], LocalSignals: [], ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("big_mod", ff, b1, b2, b3)),
            compactLayout: true);

        ElkNode bigCompound = Assert.Single(bigResult.Graph.Children, n => n.Id == "child_top_big");

        Assert.True(bigCompound.Width >= smallCompound.Width,
            $"Compound with more inner primitives should be at least as wide: big.W={bigCompound.Width}, small.W={smallCompound.Width}");
        // And both should be at least the 320 floor.
        Assert.True(smallCompound.Width >= 320);
        Assert.True(bigCompound.Width   >= 320);
    }

    [Fact]
    public void ScopedInnerIds_DoNotCollide_AcrossDifferentCompoundsWithSameSignal()
    {
        // Two separate compounds whose modules each contain a FF named "q".
        // The inner ids encode the hierarchy path so they must be distinct.
        FlipFlopPrimitive ffA = MakeFF("q");
        FlipFlopPrimitive ffB = MakeFF("q");

        HierarchyScopeInstanceViewModel instA = Child("top.a_i", "a_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1)]);
        HierarchyScopeInstanceViewModel instB = Child("top.b_i", "b_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1)]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.a_i", "top.b_i" };
        var byModule = new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            ["a_mod"] = [ffA],
            ["b_mod"] = [ffB]
        };

        ElkBuildResult r = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [], ChildScopes: [instA, instB], LocalSignals: [], ContAssigns: [],
                ExpandedPaths: expanded, PrimitivesByModule: byModule),
            compactLayout: true);

        ElkNode aFF = InnerNode(r, "child_top_a_i", "ff_top_a_i__q");
        ElkNode bFF = InnerNode(r, "child_top_b_i", "ff_top_b_i__q");
        Assert.NotEqual(aFF.Id, bFF.Id);
        // And both still dispatch as flip-flops.
        Assert.True(ElkNodeIds.IsFlipFlop(aFF.Id));
        Assert.True(ElkNodeIds.IsFlipFlop(bFF.Id));
    }

    [Fact]
    public void InnerInverter_DispatchesAsInverter_NotGenericCard()
    {
        // Defensive: every primitive kind must satisfy its Is* discriminator so the
        // renderer reaches the correct symbol drawer (bubble for inverter, triangle for
        // buffer, gate-shaped bodies for And/Or/Xor, etc.).
        InverterPrimitive inv = new("inv_y_0", "y", "a", Width: 1);
        BufferPrimitive   buf = new("buf_b_0", "b", "a", Width: 1);

        HierarchyScopeInstanceViewModel inst = Child(
            "top.log", "log_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 1)]);
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.log" };

        ElkBuildResult r = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [], ChildScopes: [inst], LocalSignals: [], ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: ByModule("log_mod", inv, buf)),
            compactLayout: true);

        ElkNode invNode = InnerNode(r, "child_top_log", "inv_top_log__y");
        ElkNode bufNode = InnerNode(r, "child_top_log", "buf_top_log__b");

        Assert.True(ElkNodeIds.IsInverter(invNode.Id));
        Assert.True(ElkNodeIds.IsBuffer(bufNode.Id));
        Assert.False(ElkNodeIds.IsInverter(bufNode.Id));
        Assert.False(ElkNodeIds.IsBuffer(invNode.Id));
    }

    [Fact]
    public void InnerPrimitive_HasFfTitleLabel_LikeOuterPrimitive()
    {
        // Inner FF's primary label must match outer ("FF <q>") so the symbol drawer's
        // title overlay reads correctly above the body.
        ElkBuildResult r = BuildFFCompoundResult(expanded: true);
        ElkNode ff = InnerNode(r, "child_top_reg", "ff_top_reg__q");
        Assert.NotNull(ff.Labels);
        Assert.Equal("FF q [8b]", ff.Labels![0].Text);
    }
}
