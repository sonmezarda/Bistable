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
    public void SubModuleInstance_SizeScalesWithBusBitRowsNotPortCount()
    {
        GateBit[] inputBits = Bits(2, 32);
        GateBit[] outputBits = Bits(100, 32);
        GateModule child = new(
            "child_bus",
            [
                new GatePort("d", GatePortDirection.Input, inputBits),
                new GatePort("q", GatePortDirection.Output, outputBits),
            ],
            [],
            []);
        GateCell instance = new(
            "u_regs",
            "child_bus",
            new Dictionary<string, GateConnection>
            {
                ["d"] = new("d", inputBits),
                ["q"] = new("q", outputBits),
            },
            new Dictionary<string, GatePortDirection>
            {
                ["d"] = GatePortDirection.Input,
                ["q"] = GatePortDirection.Output,
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        GateModule top = new(
            "top",
            [
                new GatePort("d", GatePortDirection.Input, inputBits),
                new GatePort("q", GatePortDirection.Output, outputBits),
            ],
            [instance],
            []);
        GateNetlist netlist = new(
            "top",
            new Dictionary<string, GateModule>
            {
                ["top"] = top,
                ["child_bus"] = child,
            });

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        ElkNode inst = Assert.Single(result.Graph.Children!,
            node => node.Id.StartsWith("inst_", StringComparison.Ordinal));
        Assert.True(inst.Height >= 36 + 32 * 18,
            $"Expected bus-width-height node, got {inst.Height}");
        Assert.Equal(64, inst.Ports!.Count);
    }

    [Fact]
    public void BoundaryNodes_SizeAndIndexByBitRowsNotDeclaredPorts()
    {
        GateBit[] bus = Bits(2, 8);
        GateModule top = new(
            "top",
            [
                new GatePort("bus", GatePortDirection.Input, bus),
                new GatePort("en", GatePortDirection.Input, [GateBit.Net(20)]),
                new GatePort("out_bus", GatePortDirection.Output, bus),
                new GatePort("done", GatePortDirection.Output, [GateBit.Net(21)]),
            ],
            [],
            []);
        GateNetlist netlist = new("top", new Dictionary<string, GateModule> { ["top"] = top });

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        ElkNode boundaryIn = Assert.Single(result.Graph.Children!, n => n.Id == "boundary_in");
        ElkNode boundaryOut = Assert.Single(result.Graph.Children!, n => n.Id == "boundary_out");
        Assert.True(boundaryIn.Height >= 28 + 9 * 18, $"Expected input boundary to fit 9 pin rows, got {boundaryIn.Height}");
        Assert.True(boundaryOut.Height >= 28 + 9 * 18, $"Expected output boundary to fit 9 pin rows, got {boundaryOut.Height}");
        AssertPortIndicesAreUnique(boundaryIn);
        AssertPortIndicesAreUnique(boundaryOut);
    }

    [Fact]
    public void BuildScope_WithExpandedInstance_RendersChildCellsInsideCompound()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.BuildScope(
            netlist,
            ["top"],
            new HashSet<string>(StringComparer.Ordinal) { "u_middle" });

        ElkNode middle = Assert.Single(result.Graph.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal)
              && n.Labels is { Count: > 0 } && n.Labels[0].Text == "u_middle");
        Assert.NotNull(middle.Children);
        Assert.Contains(middle.Children!, n => n.Id.StartsWith("inst_", StringComparison.Ordinal)
            && n.Labels is { Count: > 0 } && n.Labels[0].Text == "u_inner");
        Assert.Contains(middle.Children!, n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
        Assert.Contains(result.Graph.Edges!, e =>
            e.Sources.Concat(e.Targets).Any(endpoint => endpoint.Contains("u_middle__", StringComparison.Ordinal)));
    }

    [Fact]
    public void BuildScope_WithNestedExpandedInstance_RendersGrandchildCellsInsideNestedCompound()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("hierarchy.json"));

        GateNetlistElkBuildResult result = GateNetlistElkBuilder.BuildScope(
            netlist,
            ["top"],
            new HashSet<string>(StringComparer.Ordinal) { "u_middle", "u_middle/u_inner" });

        ElkNode middle = Assert.Single(result.Graph.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal)
              && n.Labels is { Count: > 0 } && n.Labels[0].Text == "u_middle");
        ElkNode inner = Assert.Single(middle.Children!,
            n => n.Id.StartsWith("inst_", StringComparison.Ordinal)
              && n.Labels is { Count: > 0 } && n.Labels[0].Text == "u_inner");
        Assert.NotNull(inner.Children);
        Assert.Contains(inner.Children!, n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
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

    private static GateBit[] Bits(int firstNetId, int count) =>
        [.. Enumerable.Range(firstNetId, count).Select(GateBit.Net)];

    private static void AssertPortIndicesAreUnique(ElkNode node)
    {
        string[] indices = [.. node.Ports!.Select(p =>
        {
            Assert.NotNull(p.LayoutOptions);
            Assert.True(p.LayoutOptions!.TryGetValue("elk.port.index", out string? index),
                $"Port {p.Id} is missing elk.port.index.");
            return index!;
        })];
        Assert.Equal(indices.Length, indices.Distinct(StringComparer.Ordinal).Count());
    }
}
