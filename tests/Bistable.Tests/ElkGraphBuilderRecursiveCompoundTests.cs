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
        // The inner FF appears with a scoped ID: "child_top_leaf_i/ff_q"
        Assert.Contains(leafNode.Children!, c => ElkNodeIds.IsFlipFlop(c.Id.Split('/')[^1]));
        Assert.Contains(leafNode.Children!, c => c.Id == "child_top_leaf_i/ff_q");
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
                BoundaryPorts: [],
                ChildScopes: [inst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                Primitives: [outerFF],
                PrimitivesByModule: ByModule("sub_mod", innerFF)),
            compactLayout: true);

        // Outer FF
        Assert.Single(result.Graph.Children, n => n.Id == "ff_q");
        // Inner FF (scoped under the compound node)
        ElkNode subNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_sub_i");
        Assert.Contains(subNode.Children!, c => c.Id == "child_top_sub_i/ff_q");
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
}
