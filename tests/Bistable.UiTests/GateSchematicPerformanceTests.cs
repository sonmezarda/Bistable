using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.UiTests;

[Trait("Category", "UI")]
public sealed class GateSchematicPerformanceTests
{
    [Fact]
    public void FitZoom_LargeGraph_IsNotClampedToFragmentedFivePercentView()
    {
        double zoom = GateSchematicCanvas.CalculateFitZoom(
            new Size(1190, 760),
            new Size(11791, 23833));

        Assert.InRange(zoom, 0.029, 0.031);
    }

    [AvaloniaFact]
    public void Rv32ClassGraph_InitialRenderAndPanStayWithinBudget()
    {
        const int cellCount = 2_000;
        GateSchematicCanvas canvas = new();
        canvas.SetGraph(BuildGraph(cellCount), EmptyModule());
        canvas.Measure(new Size(1600, 900));
        canvas.Arrange(new Rect(0, 0, 1600, 900));
        using RenderTargetBitmap target = new(new PixelSize(1600, 900));

        Stopwatch initialRender = Stopwatch.StartNew();
        target.Render(canvas);
        initialRender.Stop();

        Assert.True(
            initialRender.Elapsed < TimeSpan.FromSeconds(2),
            $"Initial {cellCount}-cell render took {initialRender.Elapsed.TotalMilliseconds:0} ms.");

        const int frames = 30;
        Stopwatch panFrames = Stopwatch.StartNew();
        for (int frame = 0; frame < frames; frame++)
        {
            canvas.PanBy(new Vector(2, 1));
            target.Render(canvas);
        }
        panFrames.Stop();

        double framesPerSecond = frames / panFrames.Elapsed.TotalSeconds;
        Assert.True(
            framesPerSecond >= 30,
            $"Pan render rate was {framesPerSecond:0.0} fps for {cellCount} cells.");
    }

    [AvaloniaFact]
    public void PinLabelLodModes_RenderWithoutChangingGraphConnectivity()
    {
        ElkGraph graph = BuildPinLabelGraph();
        GateSchematicCanvas canvas = new();
        canvas.SetGraph(graph, EmptyModule());
        canvas.Measure(new Size(800, 480));
        canvas.Arrange(new Rect(0, 0, 800, 480));
        using RenderTargetBitmap target = new(new PixelSize(800, 480));

        foreach (GatePinLabelMode mode in Enum.GetValues<GatePinLabelMode>())
        {
            canvas.SetPinLabelOptions(new GatePinLabelDisplayOptions(
                mode,
                GroupBusPinLabels: mode != GatePinLabelMode.Hidden,
                CompactZoom: 0.2,
                DetailedZoom: 0.4));
            target.Render(canvas);
        }

        Assert.Single(graph.Edges);
        Assert.Equal("net42", graph.Edges[0].Labels![0].Text);
    }

    [AvaloniaFact]
    public void BusTrunkAndIndividualModes_RenderWithoutMutatingPerBitEdges()
    {
        (ElkGraph graph, GateBusBundle bundle) = BuildBusGraph();
        GateSchematicCanvas canvas = new();
        canvas.SetGraph(graph, EmptyModule(), [bundle]);
        canvas.Measure(new Size(800, 480));
        canvas.Arrange(new Rect(0, 0, 800, 480));
        using RenderTargetBitmap target = new(new PixelSize(800, 480));

        canvas.SetBusDisplayOptions(new GateBusDisplayOptions(
            GateBusVisualizationMode.Bundled,
            TrunkMaxZoom: 0.9));
        target.Render(canvas);

        canvas.SetBusDisplayOptions(new GateBusDisplayOptions(
            GateBusVisualizationMode.Individual,
            TrunkMaxZoom: 0.9));
        target.Render(canvas);

        Assert.Equal(4, graph.Edges.Count);
        Assert.All(
            bundle.Members,
            member => Assert.Contains(graph.Edges, edge => edge.Id == member.EdgeId));
    }

    [AvaloniaFact]
    public void DenseVisiblePinLabels_RenderWithinBudget()
    {
        GateSchematicCanvas canvas = new();
        canvas.SetGraph(BuildDensePinLabelGraph(), EmptyModule());
        canvas.SetPinLabelOptions(new GatePinLabelDisplayOptions(
            GatePinLabelMode.Always,
            GroupBusPinLabels: false,
            CompactZoom: 0.05,
            DetailedZoom: 0.05));
        canvas.Measure(new Size(1400, 900));
        canvas.Arrange(new Rect(0, 0, 1400, 900));
        using RenderTargetBitmap target = new(new PixelSize(1400, 900));

        Stopwatch timer = Stopwatch.StartNew();
        target.Render(canvas);
        timer.Stop();

        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(1.5),
            $"Dense pin-label render took {timer.Elapsed.TotalMilliseconds:0} ms.");
    }

    private static ElkGraph BuildGraph(int cellCount)
    {
        const int columns = 50;
        const double stepX = 78;
        const double stepY = 54;
        ElkGraph graph = new()
        {
            Id = "rv32-render-benchmark",
            Width = columns * stepX,
            Height = Math.Ceiling(cellCount / (double)columns) * stepY,
        };

        for (int index = 0; index < cellCount; index++)
        {
            int column = index % columns;
            int row = index / columns;
            double x = column * stepX;
            double y = row * stepY;
            graph.Children.Add(new ElkNode
            {
                Id = $"gate_cell_{index}_{index}",
                X = x,
                Y = y,
                Width = 58,
                Height = 34,
                Labels =
                [
                    new ElkLabel { Text = $"cell_{index}" },
                    new ElkLabel { Text = "$_AND_" },
                ],
            });

            if (index == 0)
            {
                continue;
            }

            int previousColumn = (index - 1) % columns;
            int previousRow = (index - 1) / columns;
            graph.Edges.Add(new ElkEdge
            {
                Id = $"edge_{index}",
                Labels = [new ElkLabel { Text = $"net{index}" }],
                Sections =
                [
                    new ElkEdgeSection
                    {
                        Id = $"section_{index}",
                        StartPoint = new ElkPoint
                        {
                            X = previousColumn * stepX + 58,
                            Y = previousRow * stepY + 17,
                        },
                        EndPoint = new ElkPoint
                        {
                            X = x,
                            Y = y + 17,
                        },
                    },
                ],
            });
        }

        return graph;
    }

    private static ElkGraph BuildPinLabelGraph()
    {
        ElkGraph graph = new()
        {
            Id = "pin-label-render",
            Width = 520,
            Height = 240,
        };
        graph.Children.Add(new ElkNode
        {
            Id = "inst_u_alu_0",
            X = 180,
            Y = 60,
            Width = 180,
            Height = 120,
            Labels =
            [
                new ElkLabel { Text = "u_alu" },
                new ElkLabel { Text = "riscv_alu" },
            ],
            Ports =
            [
                new ElkPort
                {
                    Id = "inst_u_alu_0.operand_a[0]",
                    X = 0,
                    Y = 48,
                    LayoutOptions = new Dictionary<string, string> { ["elk.port.side"] = "WEST" },
                    Labels = [new ElkLabel { Text = "operand_a[0]" }],
                },
                new ElkPort
                {
                    Id = "inst_u_alu_0.result[0]",
                    X = 180,
                    Y = 48,
                    LayoutOptions = new Dictionary<string, string> { ["elk.port.side"] = "EAST" },
                    Labels = [new ElkLabel { Text = "result[0]" }],
                },
            ],
        });
        graph.Edges.Add(new ElkEdge
        {
            Id = "edge_net42",
            Labels = [new ElkLabel { Text = "net42" }],
            Sections =
            [
                new ElkEdgeSection
                {
                    Id = "section_net42",
                    StartPoint = new ElkPoint { X = 360, Y = 108 },
                    EndPoint = new ElkPoint { X = 480, Y = 108 },
                },
            ],
        });
        return graph;
    }

    private static (ElkGraph Graph, GateBusBundle Bundle) BuildBusGraph()
    {
        ElkNode source = new()
        {
            Id = "source",
            X = 40,
            Y = 100,
            Width = 100,
            Height = 120,
            Ports = [],
        };
        ElkNode target = new()
        {
            Id = "target",
            X = 500,
            Y = 100,
            Width = 100,
            Height = 120,
            Ports = [],
        };
        ElkGraph graph = new()
        {
            Id = "bus-render",
            Width = 680,
            Height = 320,
            Children = [source, target],
        };
        List<GateBusBundleMember> members = [];
        for (int bit = 3; bit >= 0; bit--)
        {
            int ordinal = 3 - bit;
            string sourcePort = $"source.d[{bit}]";
            string targetPort = $"target.q[{bit}]";
            source.Ports.Add(new ElkPort { Id = sourcePort, X = 100, Y = 30 + ordinal * 18 });
            target.Ports.Add(new ElkPort { Id = targetPort, X = 0, Y = 30 + ordinal * 18 });
            string edgeId = $"edge_bus_{bit}";
            int netId = 100 + bit;
            graph.Edges.Add(new ElkEdge
            {
                Id = edgeId,
                Sources = [sourcePort],
                Targets = [targetPort],
                Labels = [new ElkLabel { Text = $"net{netId}" }],
                LayoutOptions = new Dictionary<string, string>
                {
                    [GateBusBundleKeys.BundleIdLayoutOption] = "bundle:ui",
                },
                Sections =
                [
                    new ElkEdgeSection
                    {
                        Id = $"section_bus_{bit}",
                        StartPoint = new ElkPoint { X = 140, Y = 130 + ordinal * 18 },
                        EndPoint = new ElkPoint { X = 500, Y = 130 + ordinal * 18 },
                    },
                ],
            });
            members.Add(new GateBusBundleMember(
                bit,
                netId,
                sourcePort,
                targetPort,
                edgeId));
        }

        return (
            graph,
            new GateBusBundle(
                "bundle:ui",
                "data",
                3,
                0,
                source.Id,
                "d",
                target.Id,
                "q",
            members));
    }

    private static ElkGraph BuildDensePinLabelGraph()
    {
        const int columns = 10;
        const int rows = 8;
        ElkGraph graph = new()
        {
            Id = "dense-labels",
            Width = 1_260,
            Height = 800,
        };
        for (int index = 0; index < columns * rows; index++)
        {
            int column = index % columns;
            int row = index / columns;
            ElkNode node = new()
            {
                Id = $"inst_dense_{index}",
                X = 20 + column * 124,
                Y = 20 + row * 96,
                Width = 108,
                Height = 82,
                Labels =
                [
                    new ElkLabel { Text = $"u_{index}" },
                    new ElkLabel { Text = "dense_module" },
                    new ElkLabel { Text = $"u_{index}" },
                ],
                Ports = [],
            };
            for (int pin = 0; pin < 4; pin++)
            {
                node.Ports.Add(new ElkPort
                {
                    Id = $"{node.Id}.input_operand_{pin}",
                    X = 0,
                    Y = 42 + pin * 10,
                    LayoutOptions = new Dictionary<string, string>
                    {
                        ["elk.port.side"] = "WEST",
                    },
                    Labels = [new ElkLabel { Text = $"input_operand_{pin}" }],
                });
                node.Ports.Add(new ElkPort
                {
                    Id = $"{node.Id}.output_result_{pin}",
                    X = node.Width,
                    Y = 42 + pin * 10,
                    LayoutOptions = new Dictionary<string, string>
                    {
                        ["elk.port.side"] = "EAST",
                    },
                    Labels = [new ElkLabel { Text = $"output_result_{pin}" }],
                });
            }
            graph.Children.Add(node);
        }
        return graph;
    }

    private static GateModule EmptyModule() =>
        new(
            "rv32_render_benchmark",
            Ports: [],
            Cells: [],
            Nets: []);
}
