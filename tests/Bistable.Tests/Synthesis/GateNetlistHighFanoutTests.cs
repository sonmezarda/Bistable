using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Synthesis;

namespace Bistable.Tests.Synthesis;

public sealed class GateNetlistHighFanoutTests
{
    [Fact]
    public void Build_HighFanoutScalar_UsesBoundedBalancedTree()
    {
        const int sinkCount = 100;
        GateNetlistElkBuildResult result =
            GateNetlistElkBuilder.Build(HighFanoutScalarNetlist(sinkCount));

        ElkNode[] hubs = EnumerateNodes(result.Graph.Children)
            .Where(node => GateSyntheticNodeIds.IsFanoutHub(node.Id))
            .ToArray();
        Assert.NotEmpty(hubs);

        int maximumPhysicalFanout = result.Graph.Edges
            .SelectMany(edge => edge.Sources)
            .GroupBy(static source => source, StringComparer.Ordinal)
            .Max(static group => group.Count());
        Assert.InRange(maximumPhysicalFanout, 1, GateHighFanoutRouter.BranchingFactor);

        Assert.All(hubs, hub =>
        {
            Assert.Equal(2, hub.Ports!.Count);
            Assert.All(hub.Ports, port => Assert.True(result.PortRefs.ContainsKey(port.Id)));
        });
    }

    [Fact]
    public void Build_HighFanoutScalar_PreservesLogicalNetIdentityOnEverySegment()
    {
        GateNetlistElkBuildResult result =
            GateNetlistElkBuilder.Build(HighFanoutScalarNetlist(100));

        ElkEdge[] syntheticEdges = result.Graph.Edges
            .Where(IsSyntheticFanoutEdge)
            .ToArray();
        Assert.NotEmpty(syntheticEdges);
        Assert.All(syntheticEdges, edge =>
        {
            Assert.Equal(2, GateSchematicCanvas.TryGetEdgeNetId(edge));
            Assert.Equal(
                "true",
                edge.LayoutOptions![GateEdgeMetadataKeys.SyntheticFanoutLayoutOption]);
            Assert.All(edge.Sources, source => Assert.True(result.PortRefs.ContainsKey(source)));
            Assert.All(edge.Targets, target => Assert.True(result.PortRefs.ContainsKey(target)));
        });
    }

    [Fact]
    public void Build_ExactlyAtThreshold_DoesNotAddSyntheticTree()
    {
        GateNetlistElkBuildResult result =
            GateNetlistElkBuilder.Build(
                HighFanoutScalarNetlist(GateHighFanoutRouter.Threshold));

        Assert.DoesNotContain(
            EnumerateNodes(result.Graph.Children),
            node => GateSyntheticNodeIds.IsFanoutHub(node.Id));
        Assert.DoesNotContain(result.Graph.Edges, IsSyntheticFanoutEdge);
    }

    [Fact]
    public void Build_WideBusSource_DoesNotReplaceBitAccurateBundleEdges()
    {
        GateNetlistElkBuildResult result =
            GateNetlistElkBuilder.Build(WideBusFanoutNetlist(width: 2, sinksPerBit: 65));

        Assert.DoesNotContain(
            EnumerateNodes(result.Graph.Children),
            node => GateSyntheticNodeIds.IsFanoutHub(node.Id));
        Assert.DoesNotContain(result.Graph.Edges, IsSyntheticFanoutEdge);
        Assert.Equal(130, result.Graph.Edges.Count);
    }

    [Fact]
    public void Build_ExpandedHighFanoutScope_PlacesHubsInsideOwningCompound()
    {
        GateNetlist netlist = ExpandedHighFanoutNetlist(100);
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.BuildScope(
            netlist,
            ["top"],
            new HashSet<string>(StringComparer.Ordinal) { "u_many" });

        ElkNode compound = Assert.Single(
            result.Graph.Children,
            node => node.Id.StartsWith("inst_", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Graph.Children,
            node => GateSyntheticNodeIds.IsFanoutHub(node.Id));
        Assert.Contains(
            compound.Children!,
            node => GateSyntheticNodeIds.IsFanoutHub(node.Id));
    }

    private static bool IsSyntheticFanoutEdge(ElkEdge edge) =>
        edge.LayoutOptions?.ContainsKey(
            GateEdgeMetadataKeys.SyntheticFanoutLayoutOption) == true;

    private static IEnumerable<ElkNode> EnumerateNodes(IReadOnlyList<ElkNode> nodes)
    {
        foreach (ElkNode node in nodes)
        {
            yield return node;
            if (node.Children is { Count: > 0 })
            {
                foreach (ElkNode child in EnumerateNodes(node.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private static GateNetlist HighFanoutScalarNetlist(int sinkCount)
    {
        GateBit shared = GateBit.Net(2);
        List<GateCell> cells = new(sinkCount);
        for (int i = 0; i < sinkCount; i++)
        {
            cells.Add(Buffer($"sink_{i}", shared, GateBit.Net(1000 + i)));
        }
        GateModule top = new(
            "top",
            [new GatePort("clk", GatePortDirection.Input, [shared])],
            cells,
            []);
        return new GateNetlist(
            "top",
            new Dictionary<string, GateModule>(StringComparer.Ordinal) { ["top"] = top });
    }

    private static GateNetlist WideBusFanoutNetlist(int width, int sinksPerBit)
    {
        GateBit[] bits = Enumerable.Range(0, width)
            .Select(index => GateBit.Net(2 + index))
            .ToArray();
        List<GateCell> cells = new(width * sinksPerBit);
        for (int bitIndex = 0; bitIndex < width; bitIndex++)
        {
            for (int sinkIndex = 0; sinkIndex < sinksPerBit; sinkIndex++)
            {
                cells.Add(Buffer(
                    $"sink_{bitIndex}_{sinkIndex}",
                    bits[bitIndex],
                    GateBit.Net(1000 + bitIndex * sinksPerBit + sinkIndex)));
            }
        }
        GateModule top = new(
            "top",
            [new GatePort("input_bus", GatePortDirection.Input, bits)],
            cells,
            []);
        return new GateNetlist(
            "top",
            new Dictionary<string, GateModule>(StringComparer.Ordinal) { ["top"] = top });
    }

    private static GateNetlist ExpandedHighFanoutNetlist(int sinkCount)
    {
        GateBit childClock = GateBit.Net(2);
        List<GateCell> childCells = new(sinkCount);
        for (int i = 0; i < sinkCount; i++)
        {
            childCells.Add(Buffer($"sink_{i}", childClock, GateBit.Net(1000 + i)));
        }
        GateModule child = new(
            "many_sinks",
            [new GatePort("clk", GatePortDirection.Input, [childClock])],
            childCells,
            []);

        GateBit topClock = GateBit.Net(2);
        GateCell instance = new(
            "u_many",
            "many_sinks",
            new Dictionary<string, GateConnection>(StringComparer.Ordinal)
            {
                ["clk"] = new GateConnection("clk", [topClock]),
            },
            new Dictionary<string, GatePortDirection>(StringComparer.Ordinal)
            {
                ["clk"] = GatePortDirection.Input,
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        GateModule top = new(
            "top",
            [new GatePort("clk", GatePortDirection.Input, [topClock])],
            [instance],
            []);
        return new GateNetlist(
            "top",
            new Dictionary<string, GateModule>(StringComparer.Ordinal)
            {
                ["top"] = top,
                ["many_sinks"] = child,
            });
    }

    private static GateCell Buffer(string name, GateBit input, GateBit output) =>
        new(
            name,
            "$_BUF_",
            new Dictionary<string, GateConnection>(StringComparer.Ordinal)
            {
                ["A"] = new GateConnection("A", [input]),
                ["Y"] = new GateConnection("Y", [output]),
            },
            new Dictionary<string, GatePortDirection>(StringComparer.Ordinal)
            {
                ["A"] = GatePortDirection.Input,
                ["Y"] = GatePortDirection.Output,
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
}
