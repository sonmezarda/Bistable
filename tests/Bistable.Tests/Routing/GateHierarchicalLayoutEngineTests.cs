using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Synthesis;

namespace Bistable.Tests.Routing;

public sealed class GateHierarchicalLayoutEngineTests
{
    [Fact]
    public async Task LayoutAsync_LargeExpandedCompound_RoutesChildThenCollapsedParent()
    {
        ElkGraph input = BuildSingleCompoundGraph();
        List<LayoutRequestSnapshot> requests = [];
        GateHierarchicalLayoutEngine engine = new((graph, _) =>
        {
            ElkNode compound = Assert.Single(
                graph.Children,
                static node => node.Id == "inst_u_child_0");
            requests.Add(new LayoutRequestSnapshot(
                [.. graph.Edges.Select(static edge => edge.Id)],
                compound.Children is { Count: > 0 },
                compound.LayoutOptions?.GetValueOrDefault("elk.portConstraints")));
            ApplyDeterministicLayout(graph);
            return Task.FromResult(graph);
        });

        ElkGraph result = await engine.LayoutAsync(input, useHierarchicalLayout: true);

        Assert.Equal(2, requests.Count);
        Assert.Equal(["internal-in", "internal-out"], requests[0].EdgeIds);
        Assert.True(requests[0].HasChildren);

        Assert.Equal(["external-in", "external-out"], requests[1].EdgeIds);
        Assert.False(requests[1].HasChildren);
        Assert.Equal("FIXED_POS", requests[1].PortConstraints);

        ElkNode finalCompound = Assert.Single(
            result.Children,
            static node => node.Id == "inst_u_child_0");
        Assert.NotNull(finalCompound.Children);
        Assert.Single(finalCompound.Children!);
        Assert.Equal(4, result.Edges.Count);
        Assert.All(result.Edges, static edge => Assert.NotEmpty(edge.Sections!));
    }

    [Fact]
    public async Task LayoutAsync_NestedCompounds_RoutesLeafToRootWithoutDuplicatingEdges()
    {
        ElkGraph input = BuildNestedCompoundGraph();
        List<string[]> requestEdges = [];
        GateHierarchicalLayoutEngine engine = new((graph, _) =>
        {
            requestEdges.Add([.. graph.Edges.Select(static edge => edge.Id)]);
            ApplyDeterministicLayout(graph);
            return Task.FromResult(graph);
        });

        ElkGraph result = await engine.LayoutAsync(input, useHierarchicalLayout: true);

        Assert.Equal(3, requestEdges.Count);
        Assert.Equal(["inner-edge", "inner-return"], requestEdges[0]);
        Assert.Equal(["outer-to-inner", "inner-to-outer"], requestEdges[1]);
        Assert.Equal(["root-to-outer", "outer-to-root"], requestEdges[2]);
        Assert.Equal(
            6,
            result.Edges.Select(static edge => edge.Id).Distinct(StringComparer.Ordinal).Count());

        ElkNode outer = Assert.Single(
            result.Children,
            static node => node.Id == "inst_outer_0");
        ElkNode inner = Assert.Single(
            outer.Children!,
            static node => node.Id == "inst_outer__inner_0");
        Assert.NotNull(inner.Children);
        Assert.Single(inner.Children!);
    }

    [Fact]
    public async Task LayoutAsync_WhenHierarchicalModeDisabled_UsesSingleOriginalLayout()
    {
        ElkGraph input = BuildSingleCompoundGraph();
        int calls = 0;
        GateHierarchicalLayoutEngine engine = new((graph, _) =>
        {
            calls++;
            Assert.Same(input, graph);
            return Task.FromResult(graph);
        });

        ElkGraph result = await engine.LayoutAsync(input, useHierarchicalLayout: false);

        Assert.Same(input, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LayoutAsync_ReusesCachedCompoundAcrossParentLayouts()
    {
        GateLevelLayoutCache cache = new(new GateNetlist(
            "top",
            new Dictionary<string, GateModule>
            {
                ["top"] = new("top", [], [], []),
            }));
        int calls = 0;
        GateHierarchicalLayoutEngine engine = new(
            (graph, _) =>
            {
                calls++;
                ApplyDeterministicLayout(graph);
                return Task.FromResult(graph);
            },
            cache);

        await engine.LayoutAsync(BuildSingleCompoundGraph(), useHierarchicalLayout: true);
        Assert.Equal(2, calls);

        await engine.LayoutAsync(BuildSingleCompoundGraph(), useHierarchicalLayout: true);

        // The independently-routed child is reused; only the parent stage runs.
        Assert.Equal(3, calls);
    }

    private static void ApplyDeterministicLayout(ElkGraph graph)
    {
        int ordinal = 0;
        foreach (ElkNode node in graph.Children)
        {
            node.X = 100 + ordinal * 200;
            node.Y = 50;
            if (node.Children is { Count: > 0 })
            {
                node.Width = 320;
                node.Height = 240;
                int childOrdinal = 0;
                foreach (ElkNode child in node.Children)
                {
                    child.X = 40 + childOrdinal++ * 80;
                    child.Y = 70;
                }
                if (node.Ports is not null)
                {
                    for (int i = 0; i < node.Ports.Count; i++)
                    {
                        node.Ports[i].X = i == 0 ? 0 : node.Width;
                        node.Ports[i].Y = 30 + i * 20;
                    }
                }
            }
            ordinal++;
        }

        foreach (ElkEdge edge in graph.Edges)
        {
            edge.Sections =
            [
                new ElkEdgeSection
                {
                    Id = edge.Id + ".s0",
                    StartPoint = new ElkPoint { X = 0, Y = 10 },
                    EndPoint = new ElkPoint { X = 100, Y = 10 },
                },
            ];
        }
    }

    private static ElkGraph BuildSingleCompoundGraph()
    {
        ElkNode source = Node("source", Port("source.out"));
        ElkNode sink = Node("sink", Port("sink.in"));
        ElkNode primitive = Node(
            "gate_u_child__and_0",
            Port("gate_u_child__and_0.A"),
            Port("gate_u_child__and_0.Y"));
        ElkNode compound = Compound(
            "inst_u_child_0",
            [Port("inst_u_child_0.a"), Port("inst_u_child_0.y")],
            [primitive]);
        return new ElkGraph
        {
            Children = [source, compound, sink],
            Edges =
            [
                Edge("external-in", "source.out", "inst_u_child_0.a"),
                Edge("internal-in", "inst_u_child_0.a", "gate_u_child__and_0.A"),
                Edge("internal-out", "gate_u_child__and_0.Y", "inst_u_child_0.y"),
                Edge("external-out", "inst_u_child_0.y", "sink.in"),
            ],
        };
    }

    private static ElkGraph BuildNestedCompoundGraph()
    {
        ElkNode primitive = Node(
            "gate_outer__inner__and_0",
            Port("gate_outer__inner__and_0.A"),
            Port("gate_outer__inner__and_0.Y"));
        ElkNode inner = Compound(
            "inst_outer__inner_0",
            [Port("inst_outer__inner_0.a"), Port("inst_outer__inner_0.y")],
            [primitive]);
        ElkNode outer = Compound(
            "inst_outer_0",
            [Port("inst_outer_0.a"), Port("inst_outer_0.y")],
            [inner]);
        return new ElkGraph
        {
            Children =
            [
                Node("source", Port("source.out")),
                outer,
                Node("sink", Port("sink.in")),
            ],
            Edges =
            [
                Edge("root-to-outer", "source.out", "inst_outer_0.a"),
                Edge("outer-to-inner", "inst_outer_0.a", "inst_outer__inner_0.a"),
                Edge("inner-edge", "inst_outer__inner_0.a", "gate_outer__inner__and_0.A"),
                Edge("inner-return", "gate_outer__inner__and_0.Y", "inst_outer__inner_0.y"),
                Edge("inner-to-outer", "inst_outer__inner_0.y", "inst_outer_0.y"),
                Edge("outer-to-root", "inst_outer_0.y", "sink.in"),
            ],
        };
    }

    private static ElkNode Node(string id, params ElkPort[] ports) => new()
    {
        Id = id,
        Width = 60,
        Height = 40,
        Ports = [.. ports],
    };

    private static ElkNode Compound(
        string id,
        IReadOnlyList<ElkPort> ports,
        IReadOnlyList<ElkNode> children) => new()
    {
        Id = id,
        Width = 160,
        Height = 100,
        Ports = [.. ports],
        Children = [.. children],
        LayoutOptions = new Dictionary<string, string>
        {
            ["elk.portConstraints"] = "FIXED_ORDER",
        },
    };

    private static ElkPort Port(string id) => new()
    {
        Id = id,
        Width = 6,
        Height = 6,
    };

    private static ElkEdge Edge(string id, string source, string target) => new()
    {
        Id = id,
        Sources = [source],
        Targets = [target],
        LayoutOptions = new Dictionary<string, string>
        {
            [GateEdgeMetadataKeys.NetIdLayoutOption] = "2",
        },
    };

    private sealed record LayoutRequestSnapshot(
        IReadOnlyList<string> EdgeIds,
        bool HasChildren,
        string? PortConstraints);
}
