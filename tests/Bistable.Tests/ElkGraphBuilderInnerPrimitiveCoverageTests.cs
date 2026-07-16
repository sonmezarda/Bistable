using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 4.5 P4.5-1+2 regression coverage. Before these tests, expanding a
/// compound whose module contained joiner/splitter/tri-state/struct-fan-out
/// primitives silently dropped them — the inner namespace had no node and no
/// endpoint registration, so wires that crossed those primitives never reached
/// the compound boundary. Each test below sets up the minimum design that
/// exercises one primitive and asserts:
///   (a) the inner primitive node appears as a child of the expanded compound
///   (b) at least one ELK edge endpoint matches the expected inner @inner path
///       (proving the producer/consumer maps both registered correctly)
/// </summary>
public sealed class ElkGraphBuilderInnerPrimitiveCoverageTests
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

    private static IReadOnlyDictionary<string, IReadOnlyList<SchematicPrimitive>>
        ByModule(string moduleName, params SchematicPrimitive[] primitives) =>
        new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleName] = primitives
        };

    // ── P4.5-1: inner JOINER renders + wires up through compound boundary ───

    [Fact]
    public void ExpandedCompound_WithInnerJoiner_RendersJoinerNodeInside()
    {
        // top.cmp (compound) → leaf consumer; cmp's module has a joiner that
        // builds "bus" from {a, b} and the leaf consumes "bus".
        JoinerPrimitive joiner = new("join_bus_0", "bus", ["a", "b"], OutputWidth: 16);

        HierarchyScopeInstanceViewModel leafInst = Child(
            "top.cmp_i.leaf_i", "leaf",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("bus", "bus", true, 16)]);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 8),
                new HierarchyScopeInstancePortConnectionViewModel("b", "b", true, 8),
            ],
            grandchildren: [leafInst]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };
        var byModule = ByModule("cmp_mod", joiner);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [cmpInst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: byModule),
            compactLayout: true);

        // (a) Joiner node appears inside the expanded compound
        ElkNode cmpNode = Assert.Single(result.Graph.Children!, n => n.Id == "child_top_cmp_i");
        Assert.NotNull(cmpNode.Children);
        Assert.Contains(cmpNode.Children!, n => n.Id == "join_top_cmp_i__bus");
    }

    [Fact]
    public void ExpandedCompound_WithInnerJoiner_EmitsEdgeFromJoinerToGrandchildConsumer()
    {
        // The joiner output "bus" should connect to leaf's "bus" input port via
        // an ELK edge — which previously silently went missing.
        JoinerPrimitive joiner = new("join_bus_0", "bus", ["a", "b"], OutputWidth: 16);
        HierarchyScopeInstanceViewModel leafInst = Child(
            "top.cmp_i.leaf_i", "leaf",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("bus", "bus", true, 16)]);
        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 8),
                new HierarchyScopeInstancePortConnectionViewModel("b", "b", true, 8),
            ],
            grandchildren: [leafInst]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded, PrimitivesByModule: ByModule("cmp_mod", joiner)),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("join_top_cmp_i__bus.out")
              && e.Targets.Contains("child_top_cmp_i_leaf_i.in.bus"));
    }

    // ── P4.5-1: inner SPLITTER renders + wires up through compound boundary ─

    [Fact]
    public void ExpandedCompound_WithInnerSplitter_RendersSplitterNodeInside()
    {
        // assign nibble = bus[3:0]
        SplitterPrimitive splitter = new("split_bus_0", "nibble", "bus",
            Range: new BitRange(3, 0), InputWidth: 8, OutputWidth: 4);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("bus",    "bus",    true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("nibble", "nibble", false, 4),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded, PrimitivesByModule: ByModule("cmp_mod", splitter)),
            compactLayout: true);

        ElkNode cmpNode = Assert.Single(result.Graph.Children!, n => n.Id == "child_top_cmp_i");
        Assert.NotNull(cmpNode.Children);
        Assert.Contains(cmpNode.Children!, n => n.Id == "split_top_cmp_i__nibble");
    }

    [Fact]
    public void ExpandedCompound_WithInnerSplitter_EmitsEdgeFromCompoundInputToSplitter()
    {
        // The compound's "bus" input feeds the splitter's input; the splitter's
        // output then drives the compound's "nibble" output. Two edges expected.
        SplitterPrimitive splitter = new("split_bus_0", "nibble", "bus",
            Range: new BitRange(3, 0), InputWidth: 8, OutputWidth: 4);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("bus",    "bus",    true,  8),
                new HierarchyScopeInstancePortConnectionViewModel("nibble", "nibble", false, 4),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded, PrimitivesByModule: ByModule("cmp_mod", splitter)),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_cmp_i.in.bus")
              && e.Targets.Contains("split_top_cmp_i__nibble.in"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("split_top_cmp_i__nibble.out")
              && e.Targets.Contains("child_top_cmp_i.out.nibble"));
    }

    // ── P4.5-1: inner TRI-STATE renders inside expanded compound ───────────

    [Fact]
    public void ExpandedCompound_WithInnerTriState_RendersTriStateNodeInside()
    {
        // assign bus = en ? data : 'z;
        TriStatePrimitive ts = new("tristate_bus_0", "bus", "data", "en",
            EnableActiveHigh: true, Width: 8);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("data", "data", true, 8),
                new HierarchyScopeInstancePortConnectionViewModel("en",   "en",   true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("bus",  "bus",  false, 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded, PrimitivesByModule: ByModule("cmp_mod", ts)),
            compactLayout: true);

        ElkNode cmpNode = Assert.Single(result.Graph.Children!, n => n.Id == "child_top_cmp_i");
        Assert.NotNull(cmpNode.Children);
        Assert.Contains(cmpNode.Children!, n => n.Id == "tristate_top_cmp_i__bus");
    }

    [Fact]
    public void ExpandedCompound_WithInnerTriState_WiresDataInputAndEnableThroughBoundary()
    {
        TriStatePrimitive ts = new("tristate_bus_0", "bus", "data", "en",
            EnableActiveHigh: true, Width: 8);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("data", "data", true, 8),
                new HierarchyScopeInstancePortConnectionViewModel("en",   "en",   true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("bus",  "bus",  false, 8),
            ]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded, PrimitivesByModule: ByModule("cmp_mod", ts)),
            compactLayout: true);

        // data → tri-state.D (BufferIn slot reused)
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_cmp_i.in.data")
              && e.Targets.Contains("tristate_top_cmp_i__bus.in"));
        // en → tri-state.EN (dedicated TriStateEnable key)
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_cmp_i.in.en")
              && e.Targets.Contains("tristate_top_cmp_i__bus.en"));
        // tri-state.Y → bus (compound output)
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("tristate_top_cmp_i__bus.out")
              && e.Targets.Contains("child_top_cmp_i.out.bus"));
    }

    [Fact]
    public void ExpandedCompound_WithInnerMemory_WiresReadOutToReadSource()
    {
        // Inside an expanded compound, the MEM tile read-out must drive the RD-mem
        // source input via the @inner-scoped array signal (same wiring as top scope).
        MemoryPrimitive mem = new("mem_ram_0", "ram", CellWidth: 8, DepthHi: 15, DepthLo: 0);
        MemoryReadPrimitive read = new(
            "memrd_q_0", MemorySignal: "ram", AddressSignal: "a", OutputSignal: "q", CellWidth: 8);

        HierarchyScopeInstanceViewModel cmpInst = Child(
            "top.cmp_i", "cmp_mod",
            ports: [new HierarchyScopeInstancePortConnectionViewModel("a", "a", true, 4)]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [cmpInst], [], [], expanded,
                PrimitivesByModule: ByModule("cmp_mod", mem, read)),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Any(s => s.EndsWith(".dout")) &&
            e.Targets.Any(t => t.EndsWith(".src")));
    }
}
