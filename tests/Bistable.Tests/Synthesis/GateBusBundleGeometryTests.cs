using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Projects;

namespace Bistable.Tests.Synthesis;

public sealed class GateBusBundleGeometryTests
{
    [Fact]
    public void Build_ParallelBus_ProducesSingleTrunkAndEndpointCollectors()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildParallelBus();

        IReadOnlyDictionary<string, GateBusBundleGeometry> geometries =
            GateBusBundleGeometryBuilder.Build(
                graph,
                new Dictionary<string, GateBusBundle> { [bundle.Id] = bundle },
                GateElkGeometry.Build(graph));

        GateBusBundleGeometry geometry = Assert.Single(geometries).Value;
        Assert.Equal(bundle.Id, geometry.BundleId);
        Assert.NotEmpty(geometry.TrunkSegments);
        Assert.NotEmpty(geometry.FanSegments);
        Assert.All(
            geometry.AllSegments,
            segment => Assert.True(
                IsOrthogonal(segment),
                $"Expected orthogonal segment {segment.Start} -> {segment.End}."));

        foreach (GateBusBundleMember member in bundle.Members)
        {
            if (member.NetId != geometry.RepresentativeNetId)
            {
                Assert.Contains(
                    geometry.FanSegments,
                    segment => segment.NetId == member.NetId);
            }
        }
    }

    [Fact]
    public void Build_UsesRepresentativeElkRouteAsObstacleAwareTrunk()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildParallelBus();
        ElkEdge representative = graph.Edges.Single(edge => edge.Id == bundle.Members[2].EdgeId);

        GateBusBundleGeometry geometry = Assert.Single(
            GateBusBundleGeometryBuilder.Build(
                graph,
                new Dictionary<string, GateBusBundle> { [bundle.Id] = bundle },
                GateElkGeometry.Build(graph))).Value;

        Assert.Equal(
            GateSchematicCanvas.TryGetEdgeNetId(representative),
            geometry.RepresentativeNetId);
        Assert.Contains(
            geometry.TrunkSegments,
            segment => segment.Start == new Point(100, 76)
                && segment.End == new Point(200, 76));
        Assert.Contains(
            geometry.TrunkSegments,
            segment => segment.Start == new Point(200, 76)
                && segment.End == new Point(200, 96));
        Assert.Contains(
            geometry.TrunkSegments,
            segment => segment.Start == new Point(200, 96)
                && segment.End == new Point(300, 96));
    }

    [Fact]
    public void Build_NestedCompound_AppliesCommonOwnerOffset()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildParallelBus(
            parentOffset: new Point(400, 250));

        GateBusBundleGeometry geometry = Assert.Single(
            GateBusBundleGeometryBuilder.Build(
                graph,
                new Dictionary<string, GateBusBundle> { [bundle.Id] = bundle },
                GateElkGeometry.Build(graph))).Value;

        Assert.Contains(
            geometry.TrunkSegments,
            segment => segment.Start.X >= 500 && segment.Start.Y >= 300);
        Assert.All(
            geometry.AllSegments,
            segment =>
            {
                Assert.True(segment.Start.X >= 500);
                Assert.True(segment.End.X >= 500);
            });
    }

    [Fact]
    public void Build_WhenMemberRouteIsMissing_DoesNotEmitGeometry()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildParallelBus();
        foreach (ElkEdge edge in graph.Edges.Skip(1))
        {
            edge.Sections = null;
        }

        IReadOnlyDictionary<string, GateBusBundleGeometry> geometries =
            GateBusBundleGeometryBuilder.Build(
                graph,
                new Dictionary<string, GateBusBundle> { [bundle.Id] = bundle },
                GateElkGeometry.Build(graph));

        Assert.Empty(geometries);
    }

    [Fact]
    public void Build_WhenOnlyOneMemberRouteIsMissing_FallsBackForWholeBundle()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildParallelBus();
        graph.Edges[0].Sections = null;

        IReadOnlyDictionary<string, GateBusBundleGeometry> geometries =
            GateBusBundleGeometryBuilder.Build(
                graph,
                new Dictionary<string, GateBusBundle> { [bundle.Id] = bundle },
                GateElkGeometry.Build(graph));

        Assert.Empty(geometries);
    }

    [Theory]
    [InlineData(GateBusVisualizationMode.Automatic, 0.89, true)]
    [InlineData(GateBusVisualizationMode.Automatic, 0.90, false)]
    [InlineData(GateBusVisualizationMode.Bundled, 8.00, true)]
    [InlineData(GateBusVisualizationMode.Individual, 0.05, false)]
    public void DisplayOptions_ResolveConfiguredLod(
        GateBusVisualizationMode mode,
        double zoom,
        bool expected)
    {
        GateBusDisplayOptions options = new(mode, TrunkMaxZoom: 0.9);

        Assert.Equal(expected, options.UsesTrunks(zoom));
    }

    private static bool IsOrthogonal(GateBusGeometrySegment segment) =>
        Math.Abs(segment.Start.X - segment.End.X) < 0.001
        || Math.Abs(segment.Start.Y - segment.End.Y) < 0.001;

    private static (ElkGraph Graph, GateBusBundle Bundle) BuildParallelBus(
        Point parentOffset = default)
    {
        ElkNode source = new()
        {
            Id = "source",
            X = 0,
            Y = 20,
            Width = 100,
            Height = 100,
            Ports = [],
        };
        ElkNode target = new()
        {
            Id = "target",
            X = 300,
            Y = 40,
            Width = 100,
            Height = 100,
            Ports = [],
        };
        List<GateBusBundleMember> members = [];
        List<ElkEdge> edges = [];
        for (int bit = 3; bit >= 0; bit--)
        {
            int ordinal = 3 - bit;
            string sourcePortId = $"source.d[{bit}]";
            string targetPortId = $"target.q[{bit}]";
            source.Ports.Add(new ElkPort
            {
                Id = sourcePortId,
                X = 100,
                Y = 20 + ordinal * 18,
            });
            target.Ports.Add(new ElkPort
            {
                Id = targetPortId,
                X = 0,
                Y = 20 + ordinal * 18,
            });

            int netId = 20 + bit;
            string edgeId = $"edge_{bit}";
            double sourceY = 40 + ordinal * 18;
            double targetY = 60 + ordinal * 18;
            edges.Add(new ElkEdge
            {
                Id = edgeId,
                Sources = [sourcePortId],
                Targets = [targetPortId],
                Labels = [new ElkLabel { Text = $"net{netId}" }],
                LayoutOptions = new Dictionary<string, string>
                {
                    [GateBusBundleKeys.BundleIdLayoutOption] = "bundle:data",
                },
                Sections =
                [
                    new ElkEdgeSection
                    {
                        Id = $"section_{bit}",
                        StartPoint = new ElkPoint { X = 100, Y = sourceY },
                        BendPoints =
                        [
                            new ElkPoint { X = 200, Y = sourceY },
                            new ElkPoint { X = 200, Y = targetY },
                        ],
                        EndPoint = new ElkPoint { X = 300, Y = targetY },
                    },
                ],
            });
            members.Add(new GateBusBundleMember(
                bit,
                netId,
                sourcePortId,
                targetPortId,
                edgeId));
        }

        GateBusBundle bundle = new(
            "bundle:data",
            "data",
            3,
            0,
            source.Id,
            "d",
            target.Id,
            "q",
            members);

        ElkGraph graph = new()
        {
            Id = "root",
            Edges = edges,
            Children = parentOffset == default
                ? [source, target]
                :
                [
                    new ElkNode
                    {
                        Id = "compound",
                        X = parentOffset.X,
                        Y = parentOffset.Y,
                        Children = [source, target],
                    },
                ],
        };
        return (graph, bundle);
    }
}
