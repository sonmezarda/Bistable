using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;

namespace Bistable.Tests;

public sealed class ElkGraphBuilderTests
{
    [Fact]
    public void RoutesBoundaryInputThroughContinuousAssignmentAlias()
    {
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 32, isSigned: false);
        HierarchyScopeInstanceViewModel control = Child(
            "top.u_control",
            "u_control",
            [new HierarchyScopeInstancePortConnectionViewModel("opcode", "opcode", isInput: true, width: 7)]);
        DesignContAssign assign = new("opcode", ["instruction"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction], [control], [], [assign]),
            compactLayout: true);

        ElkEdge edge = Assert.Single(result.Graph.Edges);
        Assert.Equal("boundary_in.instruction", Assert.Single(edge.Sources));
        Assert.Equal("child_top_u_control.in.opcode", Assert.Single(edge.Targets));
        Assert.NotNull(edge.Labels);
        Assert.Equal("instruction", edge.Labels[0].Text);
        Assert.Equal("7", edge.Labels[1].Text);
    }

    [Fact]
    public void RoutesBoundaryOutputThroughContinuousAssignmentAlias()
    {
        HierarchyScopePortViewModel memAddr = new("mem_addr", SignalDirection.Output, 32, isSigned: false);
        HierarchyScopeInstanceViewModel alu = Child(
            "top.u_alu",
            "u_alu",
            [new HierarchyScopeInstancePortConnectionViewModel("addr", "alu_addr", isInput: false, width: 32)]);
        DesignContAssign assign = new("mem_addr", ["alu_addr"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([memAddr], [alu], [], [assign]),
            compactLayout: true);

        ElkEdge edge = Assert.Single(result.Graph.Edges);
        Assert.Equal("child_top_u_alu.out.addr", Assert.Single(edge.Sources));
        Assert.Equal("boundary_out.mem_addr", Assert.Single(edge.Targets));
        Assert.NotNull(edge.Labels);
        Assert.Equal("alu_addr", edge.Labels[0].Text);
        Assert.Equal("32", edge.Labels[1].Text);
    }

    [Fact]
    public void DoesNotTreatMultiSourceContinuousAssignmentAsAlias()
    {
        HierarchyScopePortViewModel instruction = new("instruction", SignalDirection.Input, 32, isSigned: false);
        HierarchyScopePortViewModel memAddr = new("mem_addr", SignalDirection.Output, 32, isSigned: false);
        HierarchyScopeInstanceViewModel registers = Child(
            "top.u_registers",
            "u_registers",
            [new HierarchyScopeInstancePortConnectionViewModel("pc", "pc", isInput: false, width: 32)]);
        DesignContAssign assign = new("mem_addr", ["pc", "instruction"]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([instruction, memAddr], [registers], [], [assign]),
            compactLayout: true);

        Assert.Empty(result.Graph.Edges);
    }

    private static HierarchyScopeInstanceViewModel Child(
        string hierarchyPath,
        string instanceName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports) =>
        new(
            hierarchyPath,
            instanceName,
            "module",
            ports.Count(static port => port.IsInput),
            ports.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            ports);
}
