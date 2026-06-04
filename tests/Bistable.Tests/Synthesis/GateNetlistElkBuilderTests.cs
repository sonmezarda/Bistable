using Bistable.App.Services.Routing.Elk;
using Bistable.Core.Synthesis;
using Bistable.Yosys;

namespace Bistable.Tests.Synthesis;

/// <summary>
/// Phase 6 P6-6: the gate-level ELK builder must produce a graph the existing
/// renderer can dispatch over. Fixtures come from the same Yosys outputs used
/// in <see cref="YosysJsonReaderTests"/>.
/// </summary>
public sealed class GateNetlistElkBuilderTests
{
    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Synthesis", "fixtures", name));

    [Fact]
    public void Build_And2_EmitsBoundaryNodesAndOneGateNode()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        Assert.NotNull(result.Graph.Children);
        // Two boundary anchors + one $_AND_ cell node.
        Assert.Contains(result.Graph.Children!, n => n.Id == "boundary_in");
        Assert.Contains(result.Graph.Children!, n => n.Id == "boundary_out");

        // Exactly one gate cell node — IDs start with "gate_" so the existing
        // ElkNodeIds.IsGate dispatcher picks them up.
        Assert.Contains(result.Graph.Children!,
            n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_And2_GateNodeLabelStartsWithGateKindToken()
    {
        // The renderer reads the first whitespace-separated token of the gate
        // node's label and dispatches on "And" / "Or" / "Xor" / ... — this
        // test is what guarantees AND symbols actually get drawn.
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        ElkNode gate = result.Graph.Children!.Single(n => n.Id.StartsWith("gate_", StringComparison.Ordinal));
        string firstToken = gate.Labels![0].Text.Split(' ')[0];
        Assert.Equal("And", firstToken);
    }

    [Fact]
    public void Build_And2_WiresAtoY_ThroughOneEdgePerInputAndOneEdgePerOutput()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        // Two boundary inputs (a, b) each drive one cell input → 2 edges.
        // One cell output (Y) drives one boundary output (y) → 1 edge.
        Assert.Equal(3, result.Graph.Edges!.Count);
        Assert.All(result.Graph.Edges, e =>
        {
            Assert.Single(e.Sources!);
            Assert.Single(e.Targets!);
        });
    }

    [Fact]
    public void Build_And2_EdgesCarryNetLabelsForHighlighting()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("and2.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        Assert.All(result.Graph.Edges!, edge =>
        {
            string label = Assert.Single(edge.Labels!).Text;
            Assert.StartsWith("net", label, StringComparison.Ordinal);
            Assert.True(int.TryParse(label[3..], out int netId));
            Assert.True(netId >= 2);
        });
    }

    [Fact]
    public void Build_DffBus_ProducesFourFlipFlopNodes()
    {
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("dff_bus.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        var ffNodes = result.Graph.Children!
            .Where(n => n.Id.StartsWith("ff_", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, ffNodes.Count);
        // Each FF must expose at least a D pin, a clock pin, and a Q pin.
        Assert.All(ffNodes, n =>
        {
            Assert.NotNull(n.Ports);
            Assert.True(n.Ports!.Count >= 3, $"FF {n.Id} should have D/C/Q pins.");
        });
    }

    [Fact]
    public void Build_WithConst_ConstantBitDoesNotCreateEdge()
    {
        // y[1] is a literal 1'b0, not a routable net — the builder must NOT
        // emit a source-less edge for it, otherwise ELK will refuse the graph.
        GateNetlist netlist = YosysJsonReader.Read(LoadFixture("with_const.json"));
        GateNetlistElkBuildResult result = GateNetlistElkBuilder.Build(netlist);

        // Every emitted edge must have both endpoints resolvable in PortRefs.
        Assert.All(result.Graph.Edges ?? [], e =>
        {
            Assert.All(e.Sources!, s => Assert.True(result.PortRefs.ContainsKey(s)));
            Assert.All(e.Targets!, t => Assert.True(result.PortRefs.ContainsKey(t)));
        });
    }

    [Fact]
    public void Build_ThrowsWhenTopModuleMissing()
    {
        // Construct a malformed netlist where the TopModule name doesn't
        // resolve in Modules. This must throw — silently picking another
        // module would mislead the user.
        GateNetlist netlist = new(
            "missing_top",
            new Dictionary<string, GateModule>
            {
                ["some_other"] = new("some_other", [], [], [])
            });
        Assert.Throws<InvalidOperationException>(() => GateNetlistElkBuilder.Build(netlist));
    }
}
