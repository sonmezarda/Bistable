using Bistable.Core.Design;
using Bistable.Core.Design.Schematic;
using Bistable.Engine;

namespace Bistable.Tests;

public sealed class EngineSchematicProjectionServiceTests
{
    [Fact]
    public void Project_ConnectsPortsThroughLogicWithoutCollapsingSignalNames()
    {
        SchematicPrimitiveList primitives = new(
            "top",
            [
                new PortPrimitive("port:a", "a", SignalDirection.Input, 1),
                new PortPrimitive("port:b", "b", SignalDirection.Input, 1),
                new PortPrimitive("port:y", "y", SignalDirection.Output, 1)
            ],
            [],
            [],
            [new GatePrimitive("gate:y", "y", GateKind.And, ["a", "b"], 1)]);

        EngineSchematicGraph graph = new EngineSchematicProjectionService().Project(primitives);

        Assert.Equal("top", graph.ModuleName);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.Collection(
            graph.Edges.OrderBy(static edge => edge.Signal),
            edge => AssertEdge(edge, "a", "port:a", "gate:y"),
            edge => AssertEdge(edge, "b", "port:b", "gate:y"),
            edge => AssertEdge(edge, "y", "gate:y", "port:y"));
    }

    [Fact]
    public void Project_CreatesExplicitNetSourceForUnresolvedConsumer()
    {
        SchematicPrimitiveList primitives = new(
            "top",
            [],
            [],
            [],
            [new InverterPrimitive("not:y", "y", "external", 1)]);

        EngineSchematicGraph graph = new EngineSchematicProjectionService().Project(primitives);

        EngineSchematicNode source = Assert.Single(graph.Nodes, static node => node.Kind == "Net");
        Assert.Equal("external", source.Label);
        AssertEdge(Assert.Single(graph.Edges), "external", source.Id, "not:y");
    }

    [Fact]
    public void Project_SkipsPlaceholderNetForUnresolvedExpressionSource()
    {
        // "?" is a decoder placeholder for an unresolved expression source; it
        // must not become a rendered net (it would show up as noise).
        SchematicPrimitiveList primitives = new(
            "top",
            [new PortPrimitive("port:y", "y", SignalDirection.Output, 1)],
            [],
            [],
            [new InverterPrimitive("not:y", "y", "?", 1)]);

        EngineSchematicGraph graph = new EngineSchematicProjectionService().Project(primitives);

        Assert.DoesNotContain(graph.Nodes, static node => node.Kind == "Net");
        Assert.DoesNotContain(graph.Nodes, static node => node.Label == "?");
    }

    [Fact]
    public void Project_SeparatesSemanticPinLabelsFromExactMuxSignalIdentity()
    {
        MuxPrimitive mux = new(
            "mux:y",
            "branch_taken",
            ["__schematic_expr_select_42"],
            [
                new MuxInput("1", new MuxSignalSource("__schematic_expr_true_42")),
                new MuxInput("0", new MuxSignalSource("alu_zero"))
            ],
            1);
        SchematicPrimitiveList primitives = new("top", [], [], [], [mux]);

        EngineSchematicNode node = Assert.Single(new EngineSchematicProjectionService().Project(primitives).Nodes,
            static candidate => candidate.Kind == "Mux");

        Assert.Equal(
            ["__schematic_expr_select_42", "__schematic_expr_true_42", "alu_zero"],
            node.Inputs);
        Assert.Equal(["S", "I0", "I1"], node.InputLabels);
        Assert.Equal(["branch_taken"], node.Outputs);
        Assert.Equal(["Y"], node.OutputLabels);
        Assert.DoesNotContain(node.InputLabels!, static label => label.StartsWith("__schematic", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_PreservesInstancePortNamesAndModuleTypeAsDisplayMetadata()
    {
        InstancePrimitive instance = new(
            "inst:u_alu",
            "u_alu",
            "riscv_alu",
            [
                new InstancePinBinding("lhs", "alu_lhs", "input", 0),
                new InstancePinBinding("rhs", "alu_rhs", "input", 1),
                new InstancePinBinding("result", "alu_result", "output", 2)
            ]);
        SchematicPrimitiveList primitives = new("top", [], [], [instance], []);

        EngineSchematicNode node = Assert.Single(
            new EngineSchematicProjectionService().Project(primitives).Nodes,
            static candidate => candidate.Kind == "Instance");

        Assert.Equal("u_alu", node.Label);
        Assert.Equal("riscv_alu", node.TypeLabel);
        Assert.Equal(["lhs", "rhs"], node.InputLabels);
        Assert.Equal(["result"], node.OutputLabels);
        Assert.Equal(["alu_lhs", "alu_rhs"], node.Inputs);
        Assert.Equal(["alu_result"], node.Outputs);
    }

    private static void AssertEdge(
        EngineSchematicEdge edge,
        string signal,
        string source,
        string target)
    {
        Assert.Equal(signal, edge.Signal);
        Assert.Equal(source, edge.SourceNodeId);
        Assert.Equal(target, edge.TargetNodeId);
    }
}
