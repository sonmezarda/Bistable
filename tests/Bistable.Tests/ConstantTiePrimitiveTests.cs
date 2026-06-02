using System.Numerics;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// P2.6-8: <c>assign x = 8'h00;</c> renders as a ConstantTiePrimitive instead
/// of a buffer with a dangling input. The decoder must recognise ConstExpr
/// sources; the ELK builder must emit a tie node with only an output port.
/// </summary>
public sealed class ConstantTiePrimitiveTests
{
    [Fact]
    public void Decoder_ContAssign_ConstExprSource_EmitsConstantTiePrimitive()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("x", 8, false, [])],
            Instances: [],
            ContAssigns: [new ContAssignAst(new VarRefLValue("x"), new ConstExpr(new BigInteger(0), 8, false))],
            SequentialBlocks: [], CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        ConstantTiePrimitive tie = Assert.Single(list.Logic.OfType<ConstantTiePrimitive>());
        Assert.Equal("x", tie.OutputSignal);
        Assert.Equal(8, tie.Width);
        Assert.Contains("0", tie.Literal);
    }

    [Theory]
    [InlineData(1, 1, "1'b1")]
    [InlineData(1, 0, "1'b0")]
    [InlineData(8, 0x42, "8'h42")]
    [InlineData(16, 0xCAFE, "16'hCAFE")]
    public void Decoder_FormatsLiteral_AccordingToWidth(int width, ulong value, string expectedFragment)
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("y", width, false, [])],
            Instances: [],
            ContAssigns: [new ContAssignAst(new VarRefLValue("y"), new ConstExpr(new BigInteger(value), width, false))],
            SequentialBlocks: [], CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        ConstantTiePrimitive tie = Assert.Single(list.Logic.OfType<ConstantTiePrimitive>());
        Assert.Equal(expectedFragment, tie.Literal);
    }

    [Fact]
    public void Decoder_NonConstantContAssign_DoesNotEmitConstantTie()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("y", 8, false, []), new SignalDecl("a", 8, false, [])],
            Instances: [],
            ContAssigns: [new ContAssignAst(new VarRefLValue("y"), new SignalRef("a"))],
            SequentialBlocks: [], CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        Assert.Empty(list.Logic.OfType<ConstantTiePrimitive>());
        Assert.Single(list.Logic.OfType<BufferPrimitive>());
    }

    [Fact]
    public void Builder_ConstantTiePrimitive_EmitsTieNode_WithOutputPortOnly()
    {
        ConstantTiePrimitive tie = new("tie_x_0", "x", "8'h00", 8);
        HierarchyScopePortViewModel xPort = new("x", SignalDirection.Output, 8, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([xPort], [], [], [], ExpandedPaths: null, Primitives: [tie]),
            compactLayout: true);

        ElkNode? node = result.Graph.Children?.FirstOrDefault(n => ElkNodeIds.IsConstantTie(n.Id));
        Assert.NotNull(node);
        Assert.NotNull(node!.Ports);
        Assert.Single(node.Ports!);                       // only output, no input
        Assert.Contains("8'h00", node.Labels![0].Text);   // title carries the literal
        Assert.Contains("x", node.Labels![0].Text);       // and the target signal name
    }

    [Fact]
    public void Builder_ConstantTie_DoesNotCoexistWithBufferForSameTarget()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("k", 8, false, [])],
            Instances: [],
            ContAssigns: [new ContAssignAst(new VarRefLValue("k"), new ConstExpr(new BigInteger(0x42), 8, false))],
            SequentialBlocks: [], CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);
        Assert.Single(list.Logic.OfType<ConstantTiePrimitive>());
        Assert.DoesNotContain(list.Logic.OfType<BufferPrimitive>(), b => b.OutputSignal == "k");
    }

    [Fact]
    public void Builder_DirectConstantInstanceInput_EmitsTieNodeAndWire()
    {
        HierarchyScopeInstanceViewModel child = Child(
            "top.u_leaf",
            "leaf",
            [new HierarchyScopeInstancePortConnectionViewModel("enable", "1'b1", isInput: true, width: 1)]);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(BoundaryPorts: [], ChildScopes: [child], LocalSignals: [], ContAssigns: []),
            compactLayout: true);

        ElkNode tie = Assert.Single(result.Graph.Children, n => ElkNodeIds.IsConstantTie(n.Id));
        Assert.Equal("1'b1", tie.Labels![0].Text);
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Single().StartsWith(tie.Id, StringComparison.Ordinal)
              && e.Targets.Contains("child_top_u_leaf.in.enable"));
    }

    [Fact]
    public void Builder_ExpandedCompoundGrandchildConstantInput_EmitsInnerTieNodeAndWire()
    {
        HierarchyScopeInstanceViewModel grandchild = Child(
            "top.u_parent.u_leaf",
            "leaf",
            [new HierarchyScopeInstancePortConnectionViewModel("enable", "1'b0", isInput: true, width: 1)]);
        HierarchyScopeInstanceViewModel parent = Child(
            "top.u_parent",
            "parent",
            ports: [],
            grandchildren: [grandchild]);

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "top.u_parent" };
        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData(
                BoundaryPorts: [],
                ChildScopes: [parent],
                LocalSignals: [],
                ContAssigns: [],
                ExpandedPaths: expanded),
            compactLayout: true);

        ElkNode parentNode = Assert.Single(result.Graph.Children, n => n.Id == "child_top_u_parent");
        Assert.NotNull(parentNode.Children);
        ElkNode tie = Assert.Single(parentNode.Children!, n => ElkNodeIds.IsConstantTie(n.Id));
        Assert.Equal("1'b0", tie.Labels![0].Text);
        Assert.Contains(result.Graph.Edges,
            e => e.Sources.Single().StartsWith(tie.Id, StringComparison.Ordinal)
              && e.Targets.Contains("child_top_u_parent_u_leaf.in.enable"));
    }

    private static HierarchyScopeInstanceViewModel Child(
        string hierarchyPath,
        string moduleName,
        IReadOnlyList<HierarchyScopeInstancePortConnectionViewModel> ports,
        IReadOnlyList<HierarchyScopeInstanceViewModel>? grandchildren = null) =>
        new(
            hierarchyPath,
            hierarchyPath.Split('.')[^1],
            moduleName,
            ports.Count(static port => port.IsInput),
            ports.Count(static port => port.IsOutput),
            exactSignalCount: 0,
            descendantSignalCount: 0,
            ports,
            childInstances: grandchildren);
}
