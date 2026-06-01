using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
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

        // P4.5-12: routing now goes through an explicit joiner node — each
        // boundary input feeds one joiner input, joiner output drives the
        // grandchild's bundled port. Visualises like Vivado's bus-merge.
        // The joiner node id contains "concat_in" and the grandchild's port name.
        bool MatchesJoinerInputEdge(ElkEdge e, string sig) =>
            e.Sources.Contains($"child_top_cmp_i.in.{sig}")
            && e.Targets.Any(t => t.Contains("concat_in") && t.Contains("__d.in."));
        foreach (string sig in new[] { "z_f_in", "n_f_in", "c_f_in", "v_f_in" })
        {
            Assert.Contains(result.Graph.Edges, e => MatchesJoinerInputEdge(e, sig));
        }
        // Joiner's bundled output drives the grandchild's bundled input port.
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_in") && s.EndsWith("__d.out"))
              && e.Targets.Contains("child_top_cmp_i_gc_i.in.d"));
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

        // P4.5-12: routing goes through an explicit fan-out splitter — the
        // grandchild's bundled output drives the fan-out's input, each fan-out
        // leg drives one boundary output. Visualises like Vivado's bus-split.
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_cmp_i_gc_i.out.out")
              && e.Targets.Any(t => t.Contains("concat_out") && t.EndsWith("__out.in")));
        foreach (string sig in new[] { "z_f_out", "n_f_out", "c_f_out", "v_f_out" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Any(s => s.Contains("concat_out") && s.Contains("__out.leg."))
                  && e.Targets.Contains($"child_top_cmp_i.out.{sig}"));
        }
    }

    [Fact]
    public void TwoLevelExpand_ArnicompTopFlagRegFlagRegisterPath_WiresFromBoundaryToInnerPrimitives()
    {
        // Reproduces user-reported regression: arnicomp_top selected, then
        // flag_reg_i expanded (concat-bound flag_register grandchild), then
        // flag_register also expanded so its internal FF + MUX become visible.
        // Two concat pins (d / out) and four 1-bit boundary signals each side.
        //
        // The expected wiring after P4.5-12 bundle-node fix:
        //  - 4 boundary inputs → joiner.in.0..3 (one edge each)
        //  - joiner.out → flag_register.in.d
        //  - flag_register.in.d → FF.D inside flag_register (via @inner namespace)
        //  - FF.Q → MUX.in0
        //  - MUX.out → flag_register.out.out
        //  - flag_register.out.out → fanout.in
        //  - fanout.leg.0..3 → 4 boundary outputs

        HierarchyScopeInstancePortConnectionViewModel concatD = new(
            portName: "d", signalName: "?", isInput: true, width: 4,
            concatParts: ["z_f_in", "n_f_in", "c_f_in", "v_f_in"]);
        HierarchyScopeInstancePortConnectionViewModel concatOut = new(
            portName: "out", signalName: "?", isInput: false, width: 4,
            concatParts: ["z_f_out", "n_f_out", "c_f_out", "v_f_out"]);

        HierarchyScopeInstanceViewModel flagRegister = Child(
            "top.flag_reg_i.flag_register", "reg_cell__W4",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("rst_n", "rst_n", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("we", "we", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("oe", "1'b1", true, 1),
                concatD, concatOut,
            ]);

        HierarchyScopeInstanceViewModel flagRegI = Child(
            "top.flag_reg_i", "flag_reg",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("clk", "clk", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("rst_n", "rst_n", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("we", "we", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("z_f_in", "z_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("n_f_in", "n_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("c_f_in", "c_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("v_f_in", "v_f_in", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("z_f_out", "z_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("n_f_out", "n_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("c_f_out", "c_f_out", false, 1),
                new HierarchyScopeInstancePortConnectionViewModel("v_f_out", "v_f_out", false, 1),
            ],
            grandchildren: [flagRegister]);

        // reg_cell__W4 module contains FF (Q=reg_q, D=d) + MUX (out=oe?reg_q:0).
        FlipFlopPrimitive ff = new("ff_reg_q_0", QSignal: "reg_q", ClockSignal: "clk",
            ClockEdge: EdgeKind.Rising, AsyncResetSignal: "rst_n", AsyncResetEdge: EdgeKind.Falling,
            DSignal: "d", Width: 4);
        MuxPrimitive mux = new("mux_out_0", OutputSignal: "out",
            SelectSignals: ["oe"],
            Inputs: [
                new MuxInput("0", new MuxConstantSource("'0", 4)),
                new MuxInput("1", new MuxSignalSource("reg_q")),
            ],
            Width: 4);

        var byModule = new Dictionary<string, IReadOnlyList<SchematicPrimitive>>(StringComparer.OrdinalIgnoreCase)
        {
            ["reg_cell__W4"] = [ff, mux]
        };

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "top.flag_reg_i",
            "top.flag_reg_i.flag_register",
        };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [flagRegI],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded,
                PrimitivesByModule: byModule),
            compactLayout: true);

        // (a) boundary inputs → joiner ports
        foreach (string sig in new[] { "z_f_in", "n_f_in", "c_f_in", "v_f_in" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Contains($"child_top_flag_reg_i.in.{sig}")
                  && e.Targets.Any(t => t.Contains("concat_in") && t.Contains("__d.in.")));
        }
        // (b) joiner.out → flag_register.in.d
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_in") && s.EndsWith("__d.out"))
              && e.Targets.Contains("child_top_flag_reg_i_flag_register.in.d"));
        // (c) flag_register's inside: flag_register.in.d → FF input
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_flag_reg_i_flag_register.in.d")
              && e.Targets.Any(t => t.StartsWith("ff_")));
        // (d) MUX output → flag_register.out.out
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.StartsWith("mux_"))
              && e.Targets.Contains("child_top_flag_reg_i_flag_register.out.out"));
        // (e) flag_register.out.out → fan-out splitter
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_flag_reg_i_flag_register.out.out")
              && e.Targets.Any(t => t.Contains("concat_out") && t.EndsWith("__out.in")));
        // (f) fan-out legs → boundary outputs
        foreach (string sig in new[] { "z_f_out", "n_f_out", "c_f_out", "v_f_out" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Any(s => s.Contains("concat_out") && s.Contains("__out.leg."))
                  && e.Targets.Contains($"child_top_flag_reg_i.out.{sig}"));
        }
    }

    [Fact]
    public void CollapsedSibling_WithConcatInputPin_RegistersOnEachConstituent()
    {
        // Top-level sibling: leaf.d({a, b}) where a/b come from another top-level wire.
        // The top-level path must use the same explicit bundle node as expanded
        // compounds; otherwise the two edges land directly on d[2b] and overlap.
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

        Assert.Contains(result.Graph.Children, n => n.Id.Contains("concat_in") && n.Id.Contains("top_leaf_i"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_src_i.out.a_out")
              && e.Targets.Any(t => t.Contains("concat_in") && t.Contains("__d.in.0")));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_src_i.out.b_out")
              && e.Targets.Any(t => t.Contains("concat_in") && t.Contains("__d.in.1")));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_in") && s.EndsWith("__d.out"))
              && e.Targets.Contains("child_top_leaf_i.in.d"));
    }

    [Fact]
    public void CollapsedSibling_WithConcatOutputPin_DrivesEachConstituentThroughFanOut()
    {
        HierarchyScopeInstancePortConnectionViewModel concatOut = new(
            portName: "out", signalName: "?", isInput: false, width: 2,
            concatParts: ["a", "b"]);

        HierarchyScopeInstanceViewModel leaf = Child(
            "top.leaf_i", "leaf_mod",
            ports: [concatOut]);

        HierarchyScopeInstanceViewModel dst = Child(
            "top.dst_i", "dst_mod",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel("a_in", "a", true, 1),
                new HierarchyScopeInstancePortConnectionViewModel("b_in", "b", true, 1),
            ]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [leaf, dst],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>()),
            compactLayout: true);

        Assert.Contains(result.Graph.Children, n => n.Id.Contains("concat_out") && n.Id.Contains("top_leaf_i"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_top_leaf_i.out.out")
              && e.Targets.Any(t => t.Contains("concat_out") && t.EndsWith("__out.in")));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_out") && s.Contains("__out.leg.0"))
              && e.Targets.Contains("child_top_dst_i.in.a_in"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_out") && s.Contains("__out.leg.1"))
              && e.Targets.Contains("child_top_dst_i.in.b_in"));
    }

    [Fact]
    public void SelectedFlagRegScope_TopLevelConcatPins_RenderJoinerAndFanOut()
    {
        // Symptom B: when the user selects flag_reg_i from the hierarchy panel,
        // flag_register is a top-level child of the selected scope. The concat
        // ports still need explicit bundle nodes at root scope; otherwise four
        // 1-bit wires land directly on d[4b] / out[4b] and overlap.
        HierarchyScopeInstanceViewModel flagRegister = Child(
            "arnicomp_top.flag_reg_i.flag_register",
            "reg_cell__W4",
            ports: [
                new HierarchyScopeInstancePortConnectionViewModel(
                    "d", "?", true, 4,
                    concatParts: ["z_f_in", "n_f_in", "c_f_in", "v_f_in"]),
                new HierarchyScopeInstancePortConnectionViewModel(
                    "out", "?", false, 4,
                    concatParts: ["z_f_out", "n_f_out", "c_f_out", "v_f_out"]),
            ]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [
                    new HierarchyScopePortViewModel("z_f_in", SignalDirection.Input, 1, false),
                    new HierarchyScopePortViewModel("n_f_in", SignalDirection.Input, 1, false),
                    new HierarchyScopePortViewModel("c_f_in", SignalDirection.Input, 1, false),
                    new HierarchyScopePortViewModel("v_f_in", SignalDirection.Input, 1, false),
                    new HierarchyScopePortViewModel("z_f_out", SignalDirection.Output, 1, false),
                    new HierarchyScopePortViewModel("n_f_out", SignalDirection.Output, 1, false),
                    new HierarchyScopePortViewModel("c_f_out", SignalDirection.Output, 1, false),
                    new HierarchyScopePortViewModel("v_f_out", SignalDirection.Output, 1, false),
                ],
                ChildScopes: [flagRegister],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>()),
            compactLayout: true);

        Assert.Contains(result.Graph.Children, n => n.Id.Contains("concat_in") && n.Id.Contains("flag_register"));
        Assert.Contains(result.Graph.Children, n => n.Id.Contains("concat_out") && n.Id.Contains("flag_register"));

        ElkNode joiner = Assert.Single(result.Graph.Children, n => n.Id.Contains("concat_in") && n.Id.Contains("flag_register"));
        ElkNode fanOut = Assert.Single(result.Graph.Children, n => n.Id.Contains("concat_out") && n.Id.Contains("flag_register"));
        Assert.True(joiner.Width >= 56);
        Assert.True(joiner.Height >= 140);
        Assert.True(fanOut.Width >= 92);
        Assert.True(fanOut.Height >= 140);

        foreach (string sig in new[] { "z_f_in", "n_f_in", "c_f_in", "v_f_in" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Contains($"boundary_in.{sig}")
                  && e.Targets.Any(t => t.Contains("concat_in") && t.Contains("__d.in.")));
        }

        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Any(s => s.Contains("concat_in") && s.EndsWith("__d.out"))
              && e.Targets.Contains("child_arnicomp_top_flag_reg_i_flag_register.in.d"));
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Contains("child_arnicomp_top_flag_reg_i_flag_register.out.out")
              && e.Targets.Any(t => t.Contains("concat_out") && t.EndsWith("__out.in")));

        foreach (string sig in new[] { "z_f_out", "n_f_out", "c_f_out", "v_f_out" })
        {
            Assert.Contains(result.Graph.Edges,
                e => e.Sources.Any(s => s.Contains("concat_out") && s.Contains("__out.leg."))
                  && e.Targets.Contains($"boundary_out.{sig}"));
        }
    }
}
