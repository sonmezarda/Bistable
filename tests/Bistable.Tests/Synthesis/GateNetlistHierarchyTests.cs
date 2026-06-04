using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6.5 Wave 2: hierarchical builder + scope navigation. Fixture
/// (hierarchy.json) was produced from:
///
///   module inner(input a, b, output y);  assign y = a & b;  endmodule
///   module middle(input a, b, c, output y);
///       wire t;
///       inner u_inner(.a(a), .b(b), .y(t));
///       assign y = t | c;
///   endmodule
///   module top(input a, b, c, output y);
///       middle u_middle(.a(a), .b(b), .c(c), .y(y));
///   endmodule
///
/// Yosys preserves the module boundary as long as `flatten` isn't called, so
/// the netlist has three separate modules and instance cells whose `type`
/// names the child module.
/// </summary>
public sealed class GateNetlistHierarchyTests
{
    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Synthesis", "fixtures", name));

    [Fact]
    public void Read_Hierarchy_RetainsAllThreeModules()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        Assert.Contains("top",    netlist.Modules.Keys);
        Assert.Contains("middle", netlist.Modules.Keys);
        Assert.Contains("inner",  netlist.Modules.Keys);
        Assert.Equal("top", netlist.TopModule);
    }

    [Fact]
    public void TopScope_RendersMiddleAsSubModuleInstanceNotPrimitive()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        // u_middle is a child module instance — it must render as an `inst_`
        // node, NOT a generic gate / unknown box.
        Assert.Contains(result.Graph.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Graph.Children!,
            n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
    }

    [Fact]
    public void SubModuleInstance_NodeLabelsCarryInstanceNameAndModuleType()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        ElkNode inst = result.Graph.Children!.Single(n => n.Id.StartsWith("inst_", StringComparison.Ordinal));
        Assert.NotNull(inst.Labels);
        Assert.True(inst.Labels!.Count >= 2,
            "Sub-module instance must carry [instanceName, moduleType] as labels.");
        Assert.Equal("u_middle", inst.Labels[0].Text);
        Assert.Equal("middle",   inst.Labels[1].Text);
    }

    [Fact]
    public void SubModuleInstance_PinsMatchChildModulePortNames()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        ElkNode inst = result.Graph.Children!.Single(n => n.Id.StartsWith("inst_", StringComparison.Ordinal));
        // middle declares ports a / b / c / y; instance must expose one ELK
        // port per declared port that has a Connection.
        Assert.NotNull(inst.Ports);
        var pinIds = inst.Ports!
            .Select(p =>
            {
                string[] parts = p.Id.Split('.');
                return parts[^1];
            })
            .ToHashSet();
        Assert.Contains("a", pinIds);
        Assert.Contains("b", pinIds);
        Assert.Contains("c", pinIds);
        Assert.Contains("y", pinIds);
    }

    [Fact]
    public void BuildScope_TopOnly_EqualsBuild()
    {
        // Passing [top] explicitly must produce the same shape as Build()
        // (which targets the top automatically).
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult viaBuild = GateNetlistElkBuilder.Build(netlist);
        GateNetlistElkBuildResult viaScope = GateNetlistElkBuilder.BuildScope(netlist, ["top"]);

        Assert.Equal(viaBuild.Graph.Children!.Count, viaScope.Graph.Children!.Count);
    }

    [Fact]
    public void BuildScope_DrillIntoMiddle_RendersInnerInstance()
    {
        // Walking into u_middle should yield middle's view — which contains
        // u_inner (sub-module instance of `inner`) plus a $_OR_ cell.
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.BuildScope(netlist, ["top", "u_middle"]);

        Assert.Contains(result.Graph.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal)
              && n.Labels is { Count: > 0 } && n.Labels[0].Text == "u_inner");
        Assert.Contains(result.Graph.Children!,
            n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildScope_DrillAllTheWay_RendersInnerAndGate()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.BuildScope(
            netlist, ["top", "u_middle", "u_inner"]);

        // inner contains exactly one $_AND_ gate.
        Assert.Single(result.Graph.Children!, n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Graph.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildScope_ThrowsOnUnknownInstance()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        Assert.Throws<InvalidOperationException>(() =>
            GateNetlistElkBuilder.BuildScope(netlist, ["top", "no_such_inst"]));
    }

    [Fact]
    public void BuildScope_ThrowsOnUnknownRootModule()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));
        Assert.Throws<InvalidOperationException>(() =>
            GateNetlistElkBuilder.BuildScope(netlist, ["fictional_top"]));
    }
}
