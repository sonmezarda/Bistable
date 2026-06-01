using System.Numerics;
using Bistable.App.Services.Routing.Elk;
using Bistable.App.ViewModels;
using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// P2.6-3: <c>assign bus = en ? data : 'z;</c> patterns must decode to a
/// <see cref="TriStatePrimitive"/> with the enable polarity preserved. The
/// ELK builder must emit three ports (data, enable, output) and wire the
/// enable signal as a consumer.
/// </summary>
public sealed class TriStatePrimitiveTests
{
    [Fact]
    public void Decoder_ActiveHighEnable_DataOnTrueBranch_EmitsActiveHighTriState()
    {
        ModuleAst module = WithContAssign(new CondExpr(
            Condition: new SignalRef("en"),
            IfTrue: new SignalRef("data"),
            IfFalse: HighImpedance(8)));

        TriStatePrimitive ts = Assert.Single(SchematicDecoder.Decode(module).Logic.OfType<TriStatePrimitive>());
        Assert.Equal("bus", ts.OutputSignal);
        Assert.Equal("data", ts.DataSignal);
        Assert.Equal("en", ts.EnableSignal);
        Assert.True(ts.EnableActiveHigh);
    }

    [Fact]
    public void Decoder_ActiveLowEnable_DataOnFalseBranch_EmitsActiveLowTriState()
    {
        ModuleAst module = WithContAssign(new CondExpr(
            Condition: new SignalRef("oe_n"),
            IfTrue: HighImpedance(8),
            IfFalse: new SignalRef("data")));

        TriStatePrimitive ts = Assert.Single(SchematicDecoder.Decode(module).Logic.OfType<TriStatePrimitive>());
        Assert.Equal("oe_n", ts.EnableSignal);
        Assert.False(ts.EnableActiveHigh);
    }

    [Fact]
    public void Decoder_NormalTernary_StillEmitsMux_NotTriState()
    {
        ModuleAst module = WithContAssign(new CondExpr(
            Condition: new SignalRef("sel"),
            IfTrue: new SignalRef("a"),
            IfFalse: new SignalRef("b")));

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);
        Assert.Empty(list.Logic.OfType<TriStatePrimitive>());
        Assert.Single(list.Logic.OfType<MuxPrimitive>());
    }

    [Fact]
    public void Builder_TriStatePrimitive_EmitsThreePorts()
    {
        TriStatePrimitive ts = new("tristate_bus_0", "bus", "data", "en", EnableActiveHigh: true, Width: 8);
        HierarchyScopePortViewModel data = new("data", SignalDirection.Input, 8, false);
        HierarchyScopePortViewModel en = new("en", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Output, 8, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([data, en, bus], [], [], [], ExpandedPaths: null, Primitives: [ts]),
            compactLayout: true);

        ElkNode? node = result.Graph.Children?.FirstOrDefault(n => ElkNodeIds.IsTriState(n.Id));
        Assert.NotNull(node);
        Assert.NotNull(node!.Ports);
        Assert.Equal(3, node.Ports!.Count);
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".in"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".en"));
        Assert.Contains(node.Ports, p => p.Id.EndsWith(".out"));
    }

    [Fact]
    public void Builder_TriStateTitle_IncludesWidthSuffixWhenMultibit()
    {
        TriStatePrimitive ts = new("tristate_bus_0", "bus", "data", "en", EnableActiveHigh: true, Width: 16);
        HierarchyScopePortViewModel data = new("data", SignalDirection.Input, 16, false);
        HierarchyScopePortViewModel en = new("en", SignalDirection.Input, 1, false);
        HierarchyScopePortViewModel bus = new("bus", SignalDirection.Output, 16, false);

        ElkBuildResult result = new ElkGraphBuilder().Build(
            new ElkScopeData([data, en, bus], [], [], [], ExpandedPaths: null, Primitives: [ts]),
            compactLayout: true);

        ElkNode node = Assert.Single(result.Graph.Children!, n => ElkNodeIds.IsTriState(n.Id));
        Assert.Contains("16b", node.Labels![0].Text);
    }

    private static ModuleAst WithContAssign(ExpressionAst source) =>
        new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [
                new SignalDecl("bus", 8, false, []),
                new SignalDecl("data", 8, false, []),
                new SignalDecl("a", 8, false, []),
                new SignalDecl("b", 8, false, []),
                new SignalDecl("en", 1, false, []),
                new SignalDecl("oe_n", 1, false, []),
                new SignalDecl("sel", 1, false, []),
            ],
            Instances: [],
            ContAssigns: [new ContAssignAst(new VarRefLValue("bus"), source)],
            SequentialBlocks: [], CombinationalBlocks: []);

    private static ConstExpr HighImpedance(int width) =>
        new(BigInteger.Zero, width, IsSigned: false, IsHighImpedance: true);
}
