using System.Numerics;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Ast.Passes;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests;

/// <summary>
/// P2.6-5: any signal driven by &gt; 1 source in the same module is flagged as
/// a <see cref="MultiDriverDiagnostic"/>. Common drivers: continuous assigns,
/// sequential block targets, instance output ports.
/// </summary>
public sealed class MultiDriverDetectionTests
{
    [Fact]
    public void Decoder_NoMultiDriver_ReturnsEmptyDiagnostics()
    {
        ModuleAst module = WithContAssigns(
            new ContAssignAst(new VarRefLValue("y"), new SignalRef("a")));
        SchematicPrimitiveList list = SchematicDecoder.Decode(module);
        Assert.NotNull(list.Diagnostics);
        Assert.Empty(list.Diagnostics!);
    }

    [Fact]
    public void Decoder_TwoContAssignsToSameSignal_FlagsAsMultiDriver()
    {
        ModuleAst module = WithContAssigns(
            new ContAssignAst(new VarRefLValue("bus"), new SignalRef("a")),
            new ContAssignAst(new VarRefLValue("bus"), new SignalRef("b")));

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        MultiDriverDiagnostic diag = Assert.Single(list.Diagnostics!);
        Assert.Equal("bus", diag.SignalName);
        Assert.Equal(2, diag.DriverDescriptions.Count);
    }

    [Fact]
    public void Decoder_ContAssignAndSequentialBlock_BothTargetingSignal_FlagsAsMultiDriver()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [
                new SignalDecl("q", 8, false, []),
                new SignalDecl("d", 8, false, []),
                new SignalDecl("clk", 1, false, [])],
            Instances: [],
            ContAssigns: [
                new ContAssignAst(new VarRefLValue("q"), new SignalRef("d")),
            ],
            SequentialBlocks: [
                new SequentialBlockAst(
                    [new EdgeTrigger(EdgeKind.Rising, "clk")],
                    new AssignAst(new VarRefLValue("q"), new SignalRef("d"), IsNonBlocking: true),
                    HasAsynchronousReset: false)
            ],
            CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        MultiDriverDiagnostic diag = Assert.Single(list.Diagnostics!);
        Assert.Equal("q", diag.SignalName);
        Assert.Contains(diag.DriverDescriptions, d => d.StartsWith("assign", System.StringComparison.Ordinal));
        Assert.Contains(diag.DriverDescriptions, d => d.StartsWith("always", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Decoder_VerilatorInternalTmps_AreExcludedFromDetection()
    {
        ModuleAst module = WithContAssigns(
            new ContAssignAst(new VarRefLValue("__VdfgTmp_h1_0"), new SignalRef("a")),
            new ContAssignAst(new VarRefLValue("__VdfgTmp_h1_0"), new SignalRef("b")));

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);
        Assert.Empty(list.Diagnostics!);
    }

    [Fact]
    public void Decoder_InstanceOutputAndContAssign_BothTargetingSignal_FlagsAsMultiDriver()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("bus", 8, false, [])],
            Instances: [
                new InstanceDecl("u1", "drv", [
                    new PortConnectionDecl("y", "bus", "out", 0)])
            ],
            ContAssigns: [new ContAssignAst(new VarRefLValue("bus"), new SignalRef("a"))],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        MultiDriverDiagnostic diag = Assert.Single(list.Diagnostics!);
        Assert.Equal("bus", diag.SignalName);
        Assert.Equal(2, diag.DriverDescriptions.Count);
        Assert.Contains(diag.DriverDescriptions, d => d.Contains("u1.y", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Decoder_SingleInstanceOutput_NoMultiDriver()
    {
        ModuleAst module = new(
            Name: "m", IsTop: true,
            Ports: [], Parameters: [],
            LocalSignals: [new SignalDecl("bus", 8, false, [])],
            Instances: [
                new InstanceDecl("u1", "drv", [
                    new PortConnectionDecl("y", "bus", "out", 0)])
            ],
            ContAssigns: [],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);
        Assert.Empty(list.Diagnostics!);
    }

    [Fact]
    public void Decoder_CombinationalAndSequentialBlocksTargetingSameSignal_FlagsAsMultiDriver()
    {
        ModuleAst module = CombinationalProjector.Project(new ModuleAst(
            Name: "m",
            IsTop: true,
            Ports: [],
            Parameters: [],
            LocalSignals: [],
            Instances: [],
            ContAssigns: [],
            SequentialBlocks:
            [
                new SequentialBlockAst(
                    [new EdgeTrigger(EdgeKind.Rising, "clk")],
                    new AssignAst(new VarRefLValue("q"), new SignalRef("d_sync"), IsNonBlocking: true),
                    HasAsynchronousReset: false)
            ],
            CombinationalBlocks:
            [
                new CombinationalBlockAst(
                    new AssignAst(new VarRefLValue("q"), new SignalRef("d_comb"), IsNonBlocking: false))
            ]));

        SchematicPrimitiveList list = SchematicDecoder.Decode(module);

        MultiDriverDiagnostic diagnostic = Assert.Single(list.Diagnostics!);
        Assert.Equal("q", diagnostic.SignalName);
        Assert.Contains(diagnostic.DriverDescriptions,
            static description => description.StartsWith("assign", StringComparison.Ordinal));
        Assert.Contains(diagnostic.DriverDescriptions,
            static description => description.StartsWith("always", StringComparison.Ordinal));
    }

    private static ModuleAst WithContAssigns(params ContAssignAst[] assigns) => new(
        Name: "m", IsTop: true,
        Ports: [], Parameters: [],
        LocalSignals: [
            new SignalDecl("y", 8, false, []),
            new SignalDecl("bus", 8, false, []),
            new SignalDecl("a", 8, false, []),
            new SignalDecl("b", 8, false, [])],
        Instances: [],
        ContAssigns: assigns,
        SequentialBlocks: [],
        CombinationalBlocks: []);
}
