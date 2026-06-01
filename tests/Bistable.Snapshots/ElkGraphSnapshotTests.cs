using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Snapshots;

// Golden-file snapshots of ElkGraphBuilder output for synthetic and sample
// scenarios. These guard against unintended structural changes in the schematic
// builder.
//
// To accept a new snapshot, run with BISTABLE_REGENERATE_SNAPSHOTS=1 then commit
// the updated golden file. Always review the diff.
[Trait("Category", "Snapshot")]
public sealed class ElkGraphSnapshotTests
{
    [Fact]
    public void OperatorAndConcatScope_Structural()
    {
        // Synthetic scope: input bus -> joiner -> output, and a separate splitter.
        // Tiny but covers joiner + splitter + boundary nodes — the three things
        // most likely to drift if someone touches ElkGraphBuilder.
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel b = new("b", SignalDirection.Input, 8, isSigned: false);
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Input, 16, isSigned: false);
        HierarchyScopePortViewModel ab = new("ab", SignalDirection.Output, 16, isSigned: false);

        HierarchyScopeInstanceViewModel consumer = Child(
            "top.u_consumer",
            "u_consumer",
            [
                new HierarchyScopeInstancePortConnectionViewModel("hi", "hi", isInput: true, width: 8),
                new HierarchyScopeInstancePortConnectionViewModel("lo", "lo", isInput: true, width: 8),
            ]);

        DesignContAssign concatAssign = new("ab", ["a", "b"], "{}");
        DesignContAssign hiSlice = new("hi", ["bus"], null, new DesignBitRange(15, 8));
        DesignContAssign loSlice = new("lo", ["bus"], null, new DesignBitRange(7, 0));

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, b, bus, ab], [consumer], [], [concatAssign, hiSlice, loSlice]),
            compactLayout: true);

        SnapshotAssert.MatchesElkGraph("synthetic-concat-and-splitter", result.Graph);
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
}
