using Avalonia;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.App.Views;
using Bistable.Core.Design;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

public sealed class ElkGraphBuilderVisualClarityTests
{
    [Fact]
    public void ExpandableChild_ReservesInvisibleHeaderRowBeforeFirstOutput()
    {
        HierarchyScopeInstanceViewModel child = new(
            hierarchyPath: "top.u_decoder",
            instanceName: "u_decoder",
            moduleName: "decoder",
            inputCount: 0,
            outputCount: 1,
            exactSignalCount: 0,
            descendantSignalCount: 0,
            portConnections:
            [
                new HierarchyScopeInstancePortConnectionViewModel(
                    "rd", "rd", isInput: false, width: 5)
            ]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [child],
                LocalSignals: [],
                ContAssigns: [],
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>
                {
                    ["decoder"] = [new BufferPrimitive("buf_rd", "rd", "source", 5)]
                }),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, static node => node.Id.StartsWith("child_", StringComparison.Ordinal));
        ElkPort spacer = Assert.Single(node.Ports!, static port => ElkGraphBuilder.IsHeaderSpacerPort(port.Id));
        ElkPort output = Assert.Single(node.Ports!, static port => port.Id.EndsWith(".out.rd", StringComparison.Ordinal));

        Assert.Equal("0", spacer.LayoutOptions!["elk.port.index"]);
        Assert.Equal("1", output.LayoutOptions!["elk.port.index"]);
        Assert.True(node.Height >= ElkGraphBuilder.ModuleHeaderHeight + 76);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ExpandableChild_RealElkLayout_PlacesFirstOutputBelowHeaderBand()
    {
        if (!IsCommandAvailable("node")) return;

        HierarchyScopeInstanceViewModel child = new(
            "top.u_decoder", "u_decoder", "decoder",
            inputCount: 0, outputCount: 1, exactSignalCount: 0, descendantSignalCount: 0,
            portConnections:
            [
                new HierarchyScopeInstancePortConnectionViewModel("rd", "rd", isInput: false, width: 5)
            ]);
        ElkBuildResult build = new ElkGraphBuilder().Build(
            new ElkScopeData(
                [], [child], [], [],
                PrimitivesByModule: new Dictionary<string, IReadOnlyList<SchematicPrimitive>>
                {
                    ["decoder"] = [new BufferPrimitive("buf_rd", "rd", "source", 5)]
                }),
            compactLayout: true);

        using ElkRunner runner = new();
        ElkGraph laidOut = runner.Layout(build.Graph);
        ElkNode node = Assert.Single(laidOut.Children, static node => node.Id.StartsWith("child_", StringComparison.Ordinal));
        ElkPort spacer = Assert.Single(node.Ports!, static port => ElkGraphBuilder.IsHeaderSpacerPort(port.Id));
        ElkPort output = Assert.Single(node.Ports!, static port => port.Id.EndsWith(".out.rd", StringComparison.Ordinal));

        Assert.True(output.Y > spacer.Y);
        Assert.True(
            output.Y >= ElkGraphBuilder.ModuleHeaderHeight,
            $"First output Y={output.Y:0.##} must be below the {ElkGraphBuilder.ModuleHeaderHeight:0}px header.");
    }

    [Theory]
    [InlineData("gate", "And")]
    [InlineData("arith", "Add")]
    [InlineData("mux", "MUX")]
    public void SyntheticPrimitive_UsesShortOperationTitle_AndKeepsFullNameForTooltip(
        string primitiveKind,
        string expectedTitle)
    {
        const string output = "__schematic_expr_result_2_mux_in_19_32";
        SchematicPrimitive primitive = primitiveKind switch
        {
            "gate" => new GatePrimitive("gate", output, GateKind.And, ["a", "b"], 1),
            "arith" => new ArithPrimitive("arith", output, ArithKind.Add, "a", "b", 32),
            _ => new MuxPrimitive(
                "mux",
                output,
                SelectSignals: ["sel"],
                Inputs: [new MuxInput("1", new MuxSignalSource("a")), new MuxInput("0", new MuxSignalSource("b"))],
                Width: 32),
        };

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts:
                [
                    new HierarchyScopePortViewModel("a", SignalDirection.Input, 32, false),
                    new HierarchyScopePortViewModel("b", SignalDirection.Input, 32, false),
                    new HierarchyScopePortViewModel("sel", SignalDirection.Input, 1, false),
                    new HierarchyScopePortViewModel(output, SignalDirection.Output, 32, false),
                ],
                ChildScopes: [],
                LocalSignals: [],
                ContAssigns: [],
                Primitives: [primitive]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children, node => node.Labels is { Count: > 1 } && node.Labels[1].Text == output);
        Assert.Equal(expectedTitle, node.Labels![0].Text);
        Assert.Contains(output, SchematicPreviewControl.BuildPrimitiveToolTip(node), StringComparison.Ordinal);
    }

    [Fact]
    public void DenseMux_ReservesReadableInputRowsAndSelectorWidth()
    {
        MuxPrimitive mux = new(
            "mux_dense",
            "result",
            SelectSignals: Enumerable.Range(0, 12).Select(static index => $"sel_{index}").ToList(),
            Inputs: Enumerable.Range(0, 16)
                .Select(static index => new MuxInput(index.ToString(), new MuxSignalSource($"in_{index}")))
                .ToList(),
            Width: 32);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([], [], [], [], Primitives: [mux]),
            compactLayout: true);
        ElkNode node = Assert.Single(result.Graph.Children, static node => ElkNodeIds.IsMux(node.Id));

        Assert.True(node.Height >= 32 + 16 * 22);
        Assert.True(node.Width >= 48 + 12 * 28);
    }

    [Fact]
    public void MuxLiveValueBadge_StaysClearOfSouthSelectorBand()
    {
        Rect body = new(0, 0, 420, 384);

        Rect badge = SchematicPreviewControl.ComputePrimitiveValueBadge(
            body,
            textWidth: 30,
            fontSize: 14,
            avoidSouthPortBand: true);

        Assert.True(body.Bottom - badge.Bottom > 100);
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
