using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

/// <summary>
/// P2.6-4: <c>inout</c> SystemVerilog ports flow data in both directions
/// (tri-state buses, I²C SDA, DDR data lanes). The builder places them in
/// the boundary-input cluster and tags each port with an "INOUT" second
/// label so the renderer can paint a bidirectional hexagon.
/// </summary>
public sealed class InoutPortTests
{
    [Fact]
    public void HierarchyScopePortViewModel_IsInOut_True_ForInOutDirection()
    {
        HierarchyScopePortViewModel port = new("sda", SignalDirection.InOut, 1, false);
        Assert.True(port.IsInOut);
        Assert.False(port.IsInput);
        Assert.False(port.IsOutput);
    }

    [Fact]
    public void Builder_InOutPort_AppearsInBoundaryInputCluster()
    {
        HierarchyScopePortViewModel sda = new("sda", SignalDirection.InOut, 8, false);
        HierarchyScopePortViewModel clk = new("clk", SignalDirection.Input, 1, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk, sda], [], [], []),
            compactLayout: true);

        ElkNode boundaryIn = Assert.Single(result.Graph.Children!, n => n.Id == ElkNodeIds.BoundaryIn);
        Assert.Contains(boundaryIn.Ports!, p => p.Id.EndsWith(".sda"));
        Assert.Contains(boundaryIn.Ports!, p => p.Id.EndsWith(".clk"));
    }

    [Fact]
    public void Builder_InOutPort_HasInoutSecondLabel_AsRenderHint()
    {
        HierarchyScopePortViewModel sda = new("sda", SignalDirection.InOut, 8, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([sda], [], [], []),
            compactLayout: true);

        ElkNode boundaryIn = Assert.Single(result.Graph.Children!, n => n.Id == ElkNodeIds.BoundaryIn);
        ElkPort sdaPort = Assert.Single(boundaryIn.Ports!, p => p.Id.EndsWith(".sda"));
        Assert.NotNull(sdaPort.Labels);
        Assert.Equal(2, sdaPort.Labels!.Count);
        Assert.Equal("INOUT", sdaPort.Labels[1].Text);
    }

    [Fact]
    public void Builder_RegularInputPort_HasNoInoutTag()
    {
        HierarchyScopePortViewModel clk = new("clk", SignalDirection.Input, 1, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([clk], [], [], []),
            compactLayout: true);

        ElkNode boundaryIn = Assert.Single(result.Graph.Children!, n => n.Id == ElkNodeIds.BoundaryIn);
        ElkPort clkPort = Assert.Single(boundaryIn.Ports!, p => p.Id.EndsWith(".clk"));
        Assert.NotNull(clkPort.Labels);
        Assert.Single(clkPort.Labels!);
        // No second "INOUT" tag on regular inputs.
    }
}
