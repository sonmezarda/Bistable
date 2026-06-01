using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// Phase 4.5 P4.5-12 regression coverage. Before this fix, a grandchild whose
/// port was bound via a concat (e.g. arnicomp's `flag_register.d({z, n, c, v})`)
/// silently lost every wire — the XML reader fell through to signalName "?" so
/// no producer/consumer key matched the outer compound's boundary signals.
/// Each test below reproduces the concat-pin pattern that broke flag_reg_i:
///   (a) compound boundary inputs feed each constituent of a grandchild's concat
///       input port via one edge per constituent;
///   (b) symmetrically, a grandchild's concat output port drives each constituent
///       boundary output of the enclosing compound.
/// </summary>
public sealed class ElkGraphBuilderConcatPinCoverageTests
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

    [Fact]
    public void ExpandedCompound_GrandchildConcatInputPort_WiresEachConstituentFromBoundary()
    {
        // Mirrors arnicomp's flag_reg_i → flag_register.d({z_f_in, n_f_in, c_f_in, v_f_in}).
        // The grandchild's "d" input gets four wires — one from each compound boundary input.
        HierarchyScopeInstancePortConnectionViewModel concatIn = new(
            portName: "d",
            signalName: "?",
            isInput: true,
            width: 4,
            concatParts: ["z_f_in", "n_f_in", "c_f_in", "v_f_in"]);

        HierarchyScopeInstanceViewModel grandchild = Child(
            "top.cmp_i.gc_i", "reg_cell",
            ports: [concatIn]);

        HierarchyScopeInstanceViewModel cmp = Child(
            "top.cmp_i", "flag_reg",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("z_f_in", "z_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("n_f_in", "n_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("c_f_in", "c_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("v_f_in", "v_f_in", true, 1),
            ],
            grandchildren: [grandchild]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [cmp],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>()),
            compactLayout: true);

        // Each compound boundary input must drive the same grandchild concat port.
        foreach (string sig in new[] { "z_f_in", "n_f_in", "c_f_in", "v_f_in" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Contains($"child_top_cmp_i.in.{sig}")
                  && e.Targets.Contains("child_top_cmp_i_gc_i.in.d"));
        }
    }

    [Fact]
    public void ExpandedCompound_GrandchildConcatOutputPort_DrivesEachBoundaryOutput()
    {
        // Symmetric to the input case: flag_register.out({z_f_out,...}) drives
        // every compound boundary output from the same physical grandchild port.
        HierarchyScopeInstancePortConnectionViewModel concatOut = new(
            portName: "out",
            signalName: "?",
            isInput: false,
            width: 4,
            concatParts: ["z_f_out", "n_f_out", "c_f_out", "v_f_out"]);

        HierarchyScopeInstanceViewModel grandchild = Child(
            "top.cmp_i.gc_i", "reg_cell",
            ports: [concatOut]);

        HierarchyScopeInstanceViewModel cmp = Child(
            "top.cmp_i", "flag_reg",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("z_f_out", "z_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("n_f_out", "n_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("c_f_out", "c_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("v_f_out", "v_f_out", false, 1),
            ],
            grandchildren: [grandchild]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.cmp_i" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [cmp],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>()),
            compactLayout: true);

        foreach (string sig in new[] { "z_f_out", "n_f_out", "c_f_out", "v_f_out" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Contains("child_top_cmp_i_gc_i.out.out")
                  && e.Targets.Contains($"child_top_cmp_i.out.{sig}"));
        }
    }

    [Fact]
    public void CollapsedSibling_WithConcatInputPin_RegistersOnEachConstituent()
    {
        // Top-level sibling: leaf.d({a, b}) where a/b come from another top-level wire.
        // Without the concat fix the leaf has no consumer entry under "a" or "b".
        HierarchyScopeInstancePortConnectionViewModel concatIn = new(
            portName: "d", signalName: "?", isInput: true, width: 2,
            concatParts: ["a", "b"]);

        HierarchyScopeInstanceViewModel leaf = Child(
            "top.leaf_i", "leaf_mod",
            ports: [concatIn]);

        HierarchyScopeInstanceViewModel src = Child(
            "top.src_i", "src_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("a_out", "a", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("b_out", "b", false, 1),
            ]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [src, leaf],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>()),
            compactLayout: true);

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_src_i.out.a_out")
              && e.Targets.Contains("child_top_leaf_i.in.d"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_src_i.out.b_out")
              && e.Targets.Contains("child_top_leaf_i.in.d"));
    }
}
