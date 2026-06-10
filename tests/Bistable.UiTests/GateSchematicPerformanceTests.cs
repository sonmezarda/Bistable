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

    private static GateModule EmptyModule() =>
        new(
            "rv32_render_benchmark",
            Ports: [],
            Cells: [],
            Nets: []);
}
