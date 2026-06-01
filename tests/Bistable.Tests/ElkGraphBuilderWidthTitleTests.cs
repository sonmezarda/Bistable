using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// P2.6-7: Primitive node titles get a <c>[Nb]</c> suffix when the relevant
/// width is greater than 1. 1-bit primitives stay un-suffixed.
/// </summary>
public sealed class ElkGraphBuilderWidthTitleTests
{
    [Theory]
    [InlineData(8, "FF q [8b]")]
    [InlineData(32, "FF q [32b]")]
    [InlineData(64, "FF q [64b]")]
    public void FlipFlopTitle_HasWidthSuffix_ForMultibit(int width, string expected)
    {
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d", Width: width);
        HierarchyScopePortViewModel clk = new("clk", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel d = new("d", SignalDirection.Input, width, false);
        HierarchyScopePortViewModel q = new("q", SignalDirection.Output, width, false);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, d, q], [], [], [], ExpandedPaths: null, Primitives: [ff]),
            compactLayout: true);
        Assert.Equal(expected, FindFirstFlipFlopLabel(result));
    }

    [Fact]
    public void FlipFlopTitle_OmitsSuffix_ForOneBit()
    {
        FlipFlopPrimitive ff = new("ff_q_0", "q", "clk", EdgeKind.Rising, null, null, "d", Width: 1);
        HierarchyScopePortViewModel clk = new("clk", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel d = new("d", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel q = new("q", SignalDirection.Output, 1, false);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, d, q], [], [], [], ExpandedPaths: null, Primitives: [ff]),
            compactLayout: true);
        Assert.Equal("FF q", FindFirstFlipFlopLabel(result));
    }

    [Fact]
    public void BufferTitle_HasWidthSuffix_ForMultibit()
    {
        BufferPrimitive buf = new("buf_y", "y", "a", Width: 16);
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 16, false);
        HierarchyScopePortViewModel y = new("y", SignalDirection.Output, 16, false);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, y], [], [], [], ExpandedPaths: null, Primitives: [buf]),
            compactLayout: true);
        ElkNode? node = result.Graph.Children?.FirstOrDefault(n => ElkNodeIds.IsBuffer(n.Id));
        Assert.NotNull(node);
        Assert.Equal("BUF y [16b]", node!.Labels?[0].Text);
    }

    [Fact]
    public void InverterTitle_HasWidthSuffix_ForMultibit()
    {
        InverterPrimitive inv = new("inv_y", "y", "a", Width: 8);
        HierarchyScopePortViewModel a = new("a", SignalDirection.Input, 8, false);
        HierarchyScopePortViewModel y = new("y", SignalDirection.Output, 8, false);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([a, y], [], [], [], ExpandedPaths: null, Primitives: [inv]),
            compactLayout: true);
        ElkNode? node = result.Graph.Children?.FirstOrDefault(n => ElkNodeIds.IsInverter(n.Id));
        Assert.NotNull(node);
        Assert.Equal("INV y [8b]", node!.Labels?[0].Text);
    }

    [Fact]
    public void LatchTitle_HasWidthSuffix_ForMultibit()
    {
        LatchPrimitive latch = new("latch_q_0", "q", "gate", "d", Width: 8);
        HierarchyScopePortViewModel gate = new("gate", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel d = new("d", SignalDirection.Input, 8, false);
        HierarchyScopePortViewModel q = new("q", SignalDirection.Output, 8, false);
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([gate, d, q], [], [], [], ExpandedPaths: null, Primitives: [latch]),
            compactLayout: true);
        ElkNode? node = result.Graph.Children?.FirstOrDefault(n => ElkNodeIds.IsLatch(n.Id));
        Assert.NotNull(node);
        Assert.Equal("L q [8b]", node!.Labels?[0].Text);
    }

    private static string? FindFirstFlipFlopLabel(ElkBuildResult result) =>
        result.Graph.Children?
            .FirstOrDefault(n => ElkNodeIds.IsFlipFlop(n.Id))?
            .Labels?
            .FirstOrDefault()?.Text;
}
