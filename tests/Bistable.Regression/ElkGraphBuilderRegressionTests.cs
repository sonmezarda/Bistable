using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Regression;

// Bug-locking tests for ElkGraphBuilder. Each test corresponds to a real bug that
// users hit. The fix lives in production code; this test guards against the same
// regression slipping back in.
//
// Reference: docs/PHASES/PHASE-0.md Section 2.
[Trait("Category", "Regression")]
public sealed class ElkGraphBuilderRegressionTests
{
    // 2026-05-23: concat (e.g. `assign c = {a, b};`) was rendered as a generic operator
    // box with the "{}" symbol instead of a dedicated joiner shape. The fix added a
    // distinct joiner node + IsConcatAssign detection in ElkGraphBuilder.
    [Fact]
    public void Concat_TwoSourceAssign_RendersJoinerNode()
    {
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel b = new("b", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel c = new("c", SignalDirection.Output, 16, isSigned: false);

        // OperatorSymbol "{}" is what the parser emits for a <concat> RHS.
        DesignContAssign assign = new("c", ["a", "b"], "{}");

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, b, c], [], [], [assign]),
            compactLayout: true);

        // A joiner node (id prefix "join_") must exist for the target signal.
        ElkNode joiner = Assert.Single(result.Graph.Children, n => n.Id == "join_c");
        Assert.True(ElkNodeIds.IsJoiner(joiner.Id), "Joiner node id should match the IsJoiner predicate.");
        Assert.False(ElkNodeIds.IsOperator(joiner.Id), "Concat must not fall back to a generic operator node.");

        // Joiner has N WEST inputs + 1 EAST output.
        Assert.NotNull(joiner.Ports);
        Assert.Equal(3, joiner.Ports!.Count);
        Assert.Contains(joiner.Ports, p => p.Id == "join_c.in.0");
        Assert.Contains(joiner.Ports, p => p.Id == "join_c.in.1");
        Assert.Contains(joiner.Ports, p => p.Id == "join_c.out");
    }

    // 2026-05-23: expanding a child module with the +/- button showed nested boxes
    // but no wires between them. Cause: CollectExpandedCompoundEndpoints did not
    // collect grandchild port connections under a scoped namespace. The fix adds
    // an "@inner::<path>::<signal>" namespace that ELK's INCLUDE_CHILDREN routes
    // through compound boundaries.
    [Fact]
    public void ExpandedCompound_InternalEdges_AppearBetweenGrandchildrenSharingSignalName()
    {
        // Outer scope: one compound child "u_outer" with two grandchildren.
        // Grandchild "u_ff" outputs "q" (Q of a flip-flop)
        // Grandchild "u_consumer" inputs "d_in" wired to the same internal signal "q"
        HierarchyScopeInstanceViewModel ff = Child(
            "top.u_outer.u_ff",
            "u_ff",
            [new HierarchyScopeInstancePortConnectionViewModel("q", "q", isInput: false, width: 8)]);
        HierarchyScopeInstanceViewModel consumer = Child(
            "top.u_outer.u_consumer",
            "u_consumer",
            [new HierarchyScopeInstancePortConnectionViewModel("d_in", "q", isInput: true, width: 8)]);
        HierarchyScopeInstanceViewModel outer = ChildWithSubInstances(
            "top.u_outer",
            "u_outer",
            portConnections: [],  // outer has no external wiring in this test
            childInstances: [ff, consumer]);

        HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase) { "top.u_outer" };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [outer], [], [], expanded),
            compactLayout: true);

        // The outer compound must be a compound node (children present).
        ElkNode outerNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_u_outer");
        Assert.NotNull(outerNode.Children);
        Assert.Equal(2, outerNode.Children!.Count);

        // An internal edge from u_ff.q (producer) to u_consumer.d_in (consumer) must exist.
        Assert.Contains(result.Graph.Edges, e =>
            e.Sources.Contains("child_top_u_outer_u_ff.out.q") &&
            e.Targets.Contains("child_top_u_outer_u_consumer.in.d_in"));
    }

    // 2026-05-23: splitter ports rendered in arbitrary order. The expected behavior
    // is MSB-first stacking so e.g. {bus[7:4], bus[3:0]} → upper slice on top.
    [Fact]
    public void Splitter_ContiguousRanges_StackInMsbOrder()
    {
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopeInstanceViewModel u_hi = Child("top.u_hi", "u_hi",
            [new HierarchyScopeInstancePortConnectionViewModel("d", "hi", isInput: true, width: 4)]);
        HierarchyScopeInstanceViewModel u_lo = Child("top.u_lo", "u_lo",
            [new HierarchyScopeInstancePortConnectionViewModel("d", "lo", isInput: true, width: 4)]);
        DesignContAssign hiSlice = new("hi", ["bus"], null, new DesignBitRange(7, 4));
        DesignContAssign loSlice = new("lo", ["bus"], null, new DesignBitRange(3, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([bus], [u_hi, u_lo], [], [hiSlice, loSlice]),
            compactLayout: true);

        ElkNode splitter = Assert.Single(result.Graph.Children, n => n.Id == "split_bus");
        Assert.NotNull(splitter.Ports);

        // Ports: index 0 = WEST input, 1+ = EAST outputs MSB-first.
        // The splitter ordering uses OrderByDescending(SourceRange.Hi) at build time.
        ElkPort firstOut = splitter.Ports![1];
        ElkPort secondOut = splitter.Ports![2];
        Assert.Equal("[7:4]", firstOut.Labels![0].Text);
        Assert.Equal("[3:0]", secondOut.Labels![0].Text);
    }

    private static HierarchyScopeInstanceViewModel Child(
        string hierarchyPath,
        string instanceName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports) =>
        new(
            hierarchyPath,
            instanceName,
            "module",
            ports.Count(static port => port.IsInput),
            ports.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            ports);

    private static HierarchyScopeInstanceViewModel ChildWithSubInstances(
        string hierarchyPath,
        string instanceName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> portConnections,
        IReadOnlyList<HierarchyScopeInstanceViewModel> childInstances) =>
        new(
            hierarchyPath,
            instanceName,
            "module",
            portConnections.Count(static port => port.IsInput),
            portConnections.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            portConnections,
            ports: null,
            localSignals: null,
            childInstances: childInstances);
}
