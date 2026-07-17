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
