using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Engine;

namespace Bistable.Tests.Engine;

/// <summary>
/// Selective inline expansion must preserve exact net identity: expanded
/// internals are namespaced with the instance name (never aliased away), the
/// collapsed Instance symbol is replaced by a Container, and the child's
/// boundary ports become pass-throughs between parent and namespaced nets.
/// </summary>
public sealed class EngineSchematicComposerTests
{
    private static ModuleAst Alu() => new(
        "alu", false,
        [
            new PortDecl("a", SignalDirection.Input, 8, false, 0),
            new PortDecl("y", SignalDirection.Output, 8, false, 1)
        ],
        [], [], [], [], [], []);

    private static ModuleAst Core() => new(
        "core", false,
        [
            new PortDecl("ci", SignalDirection.Input, 8, false, 0),
            new PortDecl("co", SignalDirection.Output, 8, false, 1)
        ],
        [], [],
        [
            new InstanceDecl("u_alu", "alu",
            [
                new PortConnectionDecl("a", "ci", "in", 0),
                new PortConnectionDecl("y", "co", "out", 1)
            ])
        ],
        [], [], []);

    private static DesignAst Design() => new(
    [
        new ModuleAst(
            "top", true,
            [
                new PortDecl("x", SignalDirection.Input, 8, false, 0),
                new PortDecl("z", SignalDirection.Output, 8, false, 1)
            ],
            [], [],
            [
                new InstanceDecl("u_alu", "alu",
                [
                    new PortConnectionDecl("a", "x", "in", 0),
                    new PortConnectionDecl("y", "z", "out", 1)
                ]),
                new InstanceDecl("u_core", "core",
                [
                    new PortConnectionDecl("ci", "x", "in", 0),
                    new PortConnectionDecl("co", "z", "out", 1)
                ])
            ],
            [], [], []),
        Core(),
        Alu()
    ]);

    private readonly EngineSchematicComposer _composer = new();

    [Fact]
    public void Compose_WithoutExpansions_MatchesPlainProjection()
    {
        DesignAst design = Design();
        EngineSchematicGraph composed = _composer.Compose(design, "top", []);
        EngineSchematicGraph projected = new EngineSchematicProjectionService()
            .Project(design.TopModule!);

        Assert.Equal(projected.ModuleName, composed.ModuleName);
        Assert.Equal(projected.Nodes.Select(static n => n.Id), composed.Nodes.Select(static n => n.Id));
        Assert.Equal(projected.Edges.Count, composed.Edges.Count);
    }

    [Fact]
    public void Compose_ExpandInstance_ReplacesSymbolWithContainerAndNamespacedInternals()
    {
        EngineSchematicGraph graph = _composer.Compose(Design(), "top", ["u_alu"]);

        Assert.DoesNotContain(graph.Nodes, static node =>
            node.Kind == "Instance" && node.Label == "u_alu");
        EngineSchematicNode container = Assert.Single(graph.Nodes, static node => node.Kind == "Container");
        Assert.Equal("container:u_alu", container.Id);
        Assert.Equal("u_alu", container.Label);
        Assert.Equal("alu", container.TypeLabel);

        // Both boundary ports live inside the container and pass the parent
        // net through to the exact namespaced internal net.
        EngineSchematicNode input = Assert.Single(graph.Nodes, static node =>
            node.Kind == "Port" && node.ContainerId == "container:u_alu" && node.TypeLabel == "input");
        Assert.Equal(["x"], input.Inputs);
        Assert.Equal(["u_alu.a"], input.Outputs);
        EngineSchematicNode output = Assert.Single(graph.Nodes, static node =>
            node.Kind == "Port" && node.ContainerId == "container:u_alu" && node.TypeLabel == "output");
        Assert.Equal(["u_alu.y"], output.Inputs);
        Assert.Equal(["z"], output.Outputs);

        // Parent wiring reaches the pass-throughs: x feeds the boundary input,
        // and the boundary output drives z's consumers (top port + u_core).
        Assert.Contains(graph.Edges, edge => edge.Signal == "x" && edge.TargetNodeId == input.Id);
        Assert.Contains(graph.Edges, edge => edge.Signal == "z" && edge.SourceNodeId == output.Id);

        // The sibling instance stays collapsed — expansion is selective.
        Assert.Contains(graph.Nodes, static node =>
            node.Kind == "Instance" && node.Label == "u_core" && node.ContainerId is null);
    }

    [Fact]
    public void Compose_NestedExpansion_ChainsContainersAndPrefixes()
    {
        EngineSchematicGraph graph = _composer.Compose(Design(), "top", ["u_core", "u_core.u_alu"]);

        EngineSchematicNode outer = Assert.Single(graph.Nodes, static node =>
            node.Kind == "Container" && node.Id == "container:u_core");
        Assert.Null(outer.ContainerId);
        EngineSchematicNode inner = Assert.Single(graph.Nodes, static node =>
            node.Kind == "Container" && node.Id == "u_core/container:u_alu");
        Assert.Equal("container:u_core", inner.ContainerId);
        Assert.Equal("u_alu", inner.Label);

        // The deep boundary input passes core's namespaced net into the doubly
        // namespaced alu net — exact hierarchical identity end to end.
        EngineSchematicNode deepInput = Assert.Single(graph.Nodes, node =>
            node.Kind == "Port" && node.ContainerId == inner.Id && node.TypeLabel == "input");
        Assert.Equal(["u_core.ci"], deepInput.Inputs);
        Assert.Equal(["u_core.u_alu.a"], deepInput.Outputs);
    }

    [Fact]
    public void Compose_ExpandingUnknownInstance_Throws()
    {
        InvalidInstancePathException exception = Assert.Throws<InvalidInstancePathException>(
            () => _composer.Compose(Design(), "top", ["u_missing"]));
        Assert.Contains("u_missing", exception.Message);
    }

    [Fact]
    public void Compose_ExpansionOfChildDocument_UsesDocumentRelativePaths()
    {
        // A hierarchical document (top.u_core) can expand its own children.
        EngineSchematicGraph graph = _composer.Compose(Design(), "top.u_core", ["u_alu"]);
        Assert.Equal("core", graph.ModuleName);
        Assert.Contains(graph.Nodes, static node => node.Id == "container:u_alu");
        Assert.Contains(graph.Nodes, static node =>
            node.Kind == "Port" && node.Outputs.Contains("u_alu.a") && node.Inputs.Contains("ci"));
    }
}
