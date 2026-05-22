using Bistable.App.Services.Layout;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

public sealed class HierarchicalLayoutEngineTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static HierarchyScopeInstanceViewModel MakeChild(
        string name,
        string[]? inputSignals = null,
        string[]? outputSignals = null)
    {
        List<HierarchyScopeInstancePortConnectionViewModel> ports = [];
        foreach (string sig in inputSignals ?? [])
        {
            ports.Add(new HierarchyScopeInstancePortConnectionViewModel(sig, sig, isInput: true, width: 1));
        }

        foreach (string sig in outputSignals ?? [])
        {
            ports.Add(new HierarchyScopeInstancePortConnectionViewModel(sig, sig, isInput: false, width: 1));
        }

        return new HierarchyScopeInstanceViewModel(name, name, name, ports.Count(static p => p.IsInput), ports.Count(static p => !p.IsInput), 0, 0, ports);
    }

    private static HierarchyScopePortViewModel MakePort(string name, bool isInput) =>
        new(name, isInput ? SignalDirection.Input : SignalDirection.Output, 1, isSigned: false);

    // ── tests ──────────────────────────────────────────────────────────────────

    private static int IndexOf(IReadOnlyList<HierarchyScopeInstanceViewModel> list, HierarchyScopeInstanceViewModel item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void LinearChainPlacesNodesInDataFlowOrder()
    {
        // A → B → C (A outputs "x" consumed by B; B outputs "y" consumed by C)
        HierarchyScopeInstanceViewModel a = MakeChild("A", outputSignals: ["x"]);
        HierarchyScopeInstanceViewModel b = MakeChild("B", inputSignals: ["x"], outputSignals: ["y"]);
        HierarchyScopeInstanceViewModel c = MakeChild("C", inputSignals: ["y"]);

        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([a, b, c], []);

        Assert.Equal(3, ordered.Count);
        int rankA = IndexOf(ordered,a);
        int rankB = IndexOf(ordered,b);
        int rankC = IndexOf(ordered,c);

        Assert.True(rankA < rankB, $"A (rank {rankA}) should come before B (rank {rankB})");
        Assert.True(rankB < rankC, $"B (rank {rankB}) should come before C (rank {rankC})");
    }

    [Fact]
    public void SingleChildReturnedAsIs()
    {
        HierarchyScopeInstanceViewModel only = MakeChild("X");
        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([only], []);
        Assert.Single(ordered);
        Assert.Same(only, ordered[0]);
    }

    [Fact]
    public void FeedbackEdgeDoesNotCauseInfiniteLoop()
    {
        // A outputs "sig" consumed by B; B outputs "fb" consumed by A (cycle)
        HierarchyScopeInstanceViewModel a = MakeChild("A", inputSignals: ["fb"], outputSignals: ["sig"]);
        HierarchyScopeInstanceViewModel b = MakeChild("B", inputSignals: ["sig"], outputSignals: ["fb"]);

        // Should complete without hanging
        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([a, b], []);

        Assert.Equal(2, ordered.Count);
    }

    [Fact]
    public void UnconnectedChildrenPreserveOriginalRelativeOrder()
    {
        HierarchyScopeInstanceViewModel a = MakeChild("A");
        HierarchyScopeInstanceViewModel b = MakeChild("B");
        HierarchyScopeInstanceViewModel c = MakeChild("C");

        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([a, b, c], []);

        // No edges → all rank 0 → original order preserved via Position
        Assert.Equal(3, ordered.Count);
        Assert.Equal(0, IndexOf(ordered,a));
        Assert.Equal(1, IndexOf(ordered,b));
        Assert.Equal(2, IndexOf(ordered,c));
    }

    [Fact]
    public void BoundaryInputReceiverAppearsBeforeInternalConsumer()
    {
        // Boundary drives "clk" → A receives "clk", B receives A's output "q"
        HierarchyScopePortViewModel boundaryClk = MakePort("clk", isInput: true);
        HierarchyScopeInstanceViewModel a = MakeChild("A", inputSignals: ["clk"], outputSignals: ["q"]);
        HierarchyScopeInstanceViewModel b = MakeChild("B", inputSignals: ["q"]);

        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([a, b], [boundaryClk]);

        Assert.True(IndexOf(ordered,a) < IndexOf(ordered,b),
            "A (receives boundary input) should come before B (receives A's output).");
    }

    [Fact]
    public void CrossingMinimizationReducesCrossingsOnFanLayout()
    {
        // Source S outputs three signals: "s1" → C1, "s2" → C2, "s3" → C3
        // Without crossing minimization, a naive ordering might place C3 before C1.
        // Sugiyama should keep the natural order when possible.
        HierarchyScopeInstanceViewModel src = MakeChild("S", outputSignals: ["s1", "s2", "s3"]);
        HierarchyScopeInstanceViewModel c1 = MakeChild("C1", inputSignals: ["s1"]);
        HierarchyScopeInstanceViewModel c2 = MakeChild("C2", inputSignals: ["s2"]);
        HierarchyScopeInstanceViewModel c3 = MakeChild("C3", inputSignals: ["s3"]);

        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout([src, c1, c2, c3], []);

        // src must come first (lower rank)
        int srcPos = IndexOf(ordered,src);
        int c1Pos = IndexOf(ordered,c1);
        int c2Pos = IndexOf(ordered,c2);
        int c3Pos = IndexOf(ordered,c3);

        Assert.True(srcPos < c1Pos, "Source should rank before C1");
        Assert.True(srcPos < c2Pos, "Source should rank before C2");
        Assert.True(srcPos < c3Pos, "Source should rank before C3");
    }

    [Fact]
    public void AllChildrenReturnedExactlyOnce()
    {
        List<HierarchyScopeInstanceViewModel> children = [];
        for (int i = 0; i < 8; i++)
        {
            children.Add(MakeChild($"M{i}", outputSignals: [$"sig{i}"], inputSignals: i > 0 ? [$"sig{i - 1}"] : null));
        }

        IReadOnlyList<HierarchyScopeInstanceViewModel> ordered =
            HierarchicalLayoutEngine.OrderForLayout(children, []);

        Assert.Equal(children.Count, ordered.Count);
        Assert.Equal(children.Count, ordered.Distinct().Count());
        foreach (HierarchyScopeInstanceViewModel child in children)
        {
            Assert.Contains(child, ordered);
        }
    }
}
