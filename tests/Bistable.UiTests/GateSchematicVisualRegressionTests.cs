using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.Views;
using Bistable.Core.Projects;
using Bistable.Core.Synthesis;

namespace Bistable.UiTests;

[Trait("Category", "UI")]
[Trait("Category", "VisualRegression")]
public sealed class GateSchematicVisualRegressionTests
{
    public static TheoryData<string, double> LodCases => new()
    {
        { "gate-pin-lod-compact-below", 0.54 },
        { "gate-pin-lod-compact-above", 0.56 },
        { "gate-pin-lod-detailed-below", 0.89 },
        { "gate-pin-lod-detailed-above", 0.91 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(LodCases))]
    public void PinLabelLodThresholds_MatchGolden(
        string goldenName,
        double zoom)
    {
        const int width = 720;
        const int height = 420;
        GateSchematicCanvas canvas = new();
        canvas.SetGraph(BuildGraph(), BuildModule());
        canvas.SetPinLabelOptions(new GatePinLabelDisplayOptions(
            GatePinLabelMode.Automatic,
            GroupBusPinLabels: false,
            CompactZoom: 0.55,
            DetailedZoom: 0.9,
            VisibilityMode: GatePinVisibilityMode.All));
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.SetViewportTransform(zoom, new Point(18, 18));
        using RenderTargetBitmap target = new(new PixelSize(width, height));

        target.Render(canvas);

        VisualGoldenAssert.Matches(
            goldenName,
            target,
            new PixelRect(0, 0, 640, 340));
    }

    private static ElkGraph BuildGraph()
    {
        ElkNode inputs = new()
        {
            Id = "boundary_in",
            X = 20,
            Y = 120,
            Width = 170,
            Height = 180,
            Labels = [new ElkLabel { Text = "IN" }],
            Ports = [],
        };
        ElkNode outputs = new()
        {
            Id = "boundary_out",
            X = 930,
            Y = 120,
            Width = 190,
            Height = 180,
            Labels = [new ElkLabel { Text = "OUT" }],
            Ports = [],
        };
        ElkNode compound = new()
        {
            Id = "inst_pipeline",
            X = 340,
            Y = 45,
            Width = 520,
            Height = 330,
            Labels =
            [
                new ElkLabel { Text = "u_pipeline" },
                new ElkLabel { Text = "rv32_execute_pipeline" },
                new ElkLabel { Text = "u_pipeline" },
            ],
            Ports = [],
            Children = [],
        };
        ElkNode child = new()
        {
            Id = "gate_u_alu",
            X = 180,
            Y = 105,
            Width = 110,
            Height = 110,
            Labels =
            [
                new ElkLabel { Text = "And" },
                new ElkLabel { Text = "$_AND_" },
                new ElkLabel { Text = "u_alu" },
            ],
            Ports =
            [
                Pin("gate_u_alu.A", "A", "WEST", 0, 38),
                Pin("gate_u_alu.B", "B", "WEST", 0, 72),
                Pin("gate_u_alu.Y", "Y", "EAST", 110, 55),
            ],
        };
        compound.Children.Add(child);

        List<ElkEdge> edges = [];
        for (int bit = 0; bit < 4; bit++)
        {
            double row = 54 + bit * 28;
            ElkPort input = Pin(
                $"boundary_in.instruction_operand_bus[{bit}]",
                $"instruction_operand_bus[{bit}]",
                "EAST",
                inputs.Width,
                row);
            ElkPort west = Pin(
                $"inst_pipeline.operand[{bit}]",
                $"operand[{bit}]",
                "WEST",
                0,
                row + 20);
            ElkPort east = Pin(
                $"inst_pipeline.result[{bit}]",
                $"result[{bit}]",
                "EAST",
                compound.Width,
                row + 20);
            ElkPort output = Pin(
                $"boundary_out.writeback_result_bus[{bit}]",
                $"writeback_result_bus[{bit}]",
                "WEST",
                0,
                row);
            inputs.Ports.Add(input);
            compound.Ports.Add(west);
            compound.Ports.Add(east);
            outputs.Ports.Add(output);

            edges.Add(Edge(
                $"input_{bit}",
                100 + bit,
                input.Id,
                west.Id,
                inputs.X + input.X,
                inputs.Y + input.Y,
                compound.X + west.X,
                compound.Y + west.Y));
            edges.Add(Edge(
                $"output_{bit}",
                200 + bit,
                east.Id,
                output.Id,
                compound.X + east.X,
                compound.Y + east.Y,
                outputs.X + output.X,
                outputs.Y + output.Y));
        }

        return new ElkGraph
        {
            Id = "gate-visual-regression",
            Width = 1_150,
            Height = 410,
            Children = [inputs, compound, outputs],
            Edges = edges,
        };
    }

    private static GateModule BuildModule() =>
        new(
            "top",
            Ports: [],
            Cells:
            [
                new GateCell(
                    "u_pipeline",
                    "rv32_execute_pipeline",
                    new Dictionary<string, GateConnection>(),
                    new Dictionary<string, GatePortDirection>
                    {
                        ["operand"] = GatePortDirection.Input,
                        ["result"] = GatePortDirection.Output,
                    },
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
                new GateCell(
                    "u_alu",
                    "$_AND_",
                    new Dictionary<string, GateConnection>(),
                    new Dictionary<string, GatePortDirection>
                    {
                        ["A"] = GatePortDirection.Input,
                        ["B"] = GatePortDirection.Input,
                        ["Y"] = GatePortDirection.Output,
                    },
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>()),
            ],
            Nets: []);

    private static ElkPort Pin(
        string id,
        string label,
        string side,
        double x,
        double y) =>
        new()
        {
            Id = id,
            X = x,
            Y = y,
            Width = 1,
            Height = 1,
            Labels = [new ElkLabel { Text = label }],
            LayoutOptions = new Dictionary<string, string>
            {
                ["elk.port.side"] = side,
            },
        };

    private static ElkEdge Edge(
        string id,
        int netId,
        string source,
        string target,
        double sourceX,
        double sourceY,
        double targetX,
        double targetY) =>
        new()
        {
            Id = id,
            Sources = [source],
            Targets = [target],
            Labels = [new ElkLabel { Text = $"net{netId}" }],
            Sections =
            [
                new ElkEdgeSection
                {
                    Id = id + "_section",
                    StartPoint = new ElkPoint { X = sourceX, Y = sourceY },
                    EndPoint = new ElkPoint { X = targetX, Y = targetY },
                    BendPoints =
                    [
                        new ElkPoint
                        {
                            X = (sourceX + targetX) / 2,
                            Y = sourceY,
                        },
                        new ElkPoint
                        {
                            X = (sourceX + targetX) / 2,
                            Y = targetY,
                        },
                    ],
                },
            ],
        };
}
