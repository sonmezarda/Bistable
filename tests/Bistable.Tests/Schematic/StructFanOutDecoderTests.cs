using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;

namespace Bistable.Tests.Schematic;

/// <summary>
/// Phase 2 P2-11: <see cref="SchematicDecoder"/> emits <see cref="StructFanOutPrimitive"/>
/// when a packed-struct signal in the scope is read by ≥ 1 consumer (contassign or
/// instance pin). Legs carry the recovered field name and the consumer list.
/// </summary>
public sealed class StructFanOutDecoderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static StructTypeDecl SampleStruct(params (string Name, int Width)[] fields)
    {
        int total = fields.Sum(f => f.Width);
        // LSB-first offset assignment matching the reader (last declared field = Lo 0)
        List<StructFieldDecl> fieldDecls = [];
        int lo = 0;
        for (int i = fields.Length - 1; i >= 0; i--)
        {
            fieldDecls.Add(new StructFieldDecl(fields[i].Name, lo, fields[i].Width));
            lo += fields[i].Width;
        }
        fieldDecls.Reverse();
        return new StructTypeDecl("test_pkg::s_t", total, fieldDecls);
    }

    private static ModuleAst MakeModule(
        StructTypeDecl? structType,
        IReadOnlyList<ContAssignAst>? contAssigns = null,
        IReadOnlyList<InstanceDecl>? instances = null,
        string signalName = "ctrl")
    {
        SignalDecl signal = new(signalName, structType?.TotalWidth ?? 8, false, [],
            IsRegistered: false, StructType: structType);

        return new ModuleAst(
            Name: "top",
            IsTop: true,
            Ports: [],
            Parameters: [],
            LocalSignals: [signal],
            Instances: instances ?? [],
            ContAssigns: contAssigns ?? [],
            SequentialBlocks: [],
            CombinationalBlocks: []);
    }

    // ── Happy paths ───────────────────────────────────────────────────────

    [Fact]
    public void StructFanOut_ContAssignReadsField_EmitsLegWithFieldName()
    {
        StructTypeDecl ctrl = SampleStruct(("hi", 4), ("mid", 2), ("lo", 1));
        // assign sliced_lo = ctrl[0:0];  (matches the "lo" field)
        ContAssignAst ca = new(
            new VarRefLValue("sliced_lo"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(0, 0)));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [ca]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        StructFanOutLeg leg = Assert.Single(fanOut.Legs);
        Assert.Equal("lo", leg.FieldName);
        Assert.Equal("sliced_lo", Assert.Single(leg.Consumers));
    }

    [Fact]
    public void StructFanOut_MultipleConsumers_AllListedUnderSameLeg()
    {
        StructTypeDecl ctrl = SampleStruct(("hi", 4), ("lo", 1));
        // Two contassigns both reading the "hi" field
        ContAssignAst ca1 = new(new VarRefLValue("t1"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(4, 1)));
        ContAssignAst ca2 = new(new VarRefLValue("t2"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(4, 1)));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [ca1, ca2]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        StructFanOutLeg leg = Assert.Single(fanOut.Legs);
        Assert.Equal("hi", leg.FieldName);
        Assert.Equal(new[] { "t1", "t2" }, leg.Consumers);
    }

    [Fact]
    public void StructFanOut_MultipleDistinctFields_EmitsMultipleLegs()
    {
        StructTypeDecl ctrl = SampleStruct(("hi", 4), ("lo", 1));
        ContAssignAst caHi = new(new VarRefLValue("t_hi"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(4, 1)));
        ContAssignAst caLo = new(new VarRefLValue("t_lo"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(0, 0)));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [caHi, caLo]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        Assert.Equal(2, fanOut.Legs.Count);
        // Legs are sorted by descending Hi (MSB-first)
        Assert.Equal("hi", fanOut.Legs[0].FieldName);
        Assert.Equal("lo", fanOut.Legs[1].FieldName);
    }

    [Fact]
    public void StructFanOut_InstancePinConsumer_AppearsAsInstanceDotPort()
    {
        StructTypeDecl ctrl = SampleStruct(("ops", 2), ("we", 1));
        InstanceDecl alu = new(
            InstanceName: "alu_i",
            ModuleName: "alu",
            PortConnections: [
                new PortConnectionDecl("ops", "ctrl", "in", 1, SignalRange: new BitRange(2, 1))
            ]);

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, instances: [alu]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        StructFanOutLeg leg = Assert.Single(fanOut.Legs);
        Assert.Equal("ops", leg.FieldName);
        Assert.Equal("alu_i.ops", Assert.Single(leg.Consumers));
    }

    [Fact]
    public void StructFanOut_MixedConsumerKinds_BothShowUp()
    {
        // One contassign + one instance pin reading the SAME field
        StructTypeDecl ctrl = SampleStruct(("we", 1));
        ContAssignAst ca = new(new VarRefLValue("we_alias"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(0, 0)));
        InstanceDecl inst = new("acc_i", "acc",
            [new PortConnectionDecl("en", "ctrl", "in", 1, new BitRange(0, 0))]);

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [ca], [inst]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        StructFanOutLeg leg = Assert.Single(fanOut.Legs);
        Assert.Equal(2, leg.Consumers.Count);
        Assert.Contains("we_alias", leg.Consumers);
        Assert.Contains("acc_i.en", leg.Consumers);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void StructWithoutConsumers_DoesNotEmitFanOut()
    {
        StructTypeDecl ctrl = SampleStruct(("a", 1), ("b", 1));
        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl));

        Assert.Empty(result.Logic.OfType<StructFanOutPrimitive>());
    }

    [Fact]
    public void NonStructSignal_NoFanOutEvenWithBitSelectConsumers()
    {
        ContAssignAst ca = new(new VarRefLValue("y"),
            new BitSelectExpr(new SignalRef("bus"), new BitRange(3, 0)));

        ModuleAst module = new(
            Name: "top", IsTop: true,
            Ports: [],
            Parameters: [],
            LocalSignals: [new SignalDecl("bus", 8, false, [])],   // no StructType
            Instances: [],
            ContAssigns: [ca],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList result = SchematicDecoder.Decode(module);
        Assert.Empty(result.Logic.OfType<StructFanOutPrimitive>());
        // Falls back to legacy splitter behaviour
        Assert.Single(result.Logic.OfType<SplitterPrimitive>());
    }

    [Fact]
    public void UnknownFieldRange_LegLabeledByBitRange()
    {
        // A slice that doesn't align with any declared field — fall back to range label
        StructTypeDecl ctrl = SampleStruct(("hi", 4), ("lo", 4));
        ContAssignAst ca = new(new VarRefLValue("mid"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(5, 2)));  // straddles fields

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [ca]));

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        StructFanOutLeg leg = Assert.Single(fanOut.Legs);
        Assert.Equal("[5:2]", leg.FieldName);   // BitRange.ToString() fallback
    }

    [Fact]
    public void StructFanOut_SuppressesSplitterPrimitive_ForSameTarget()
    {
        // When a fan-out leg owns a contassign target, the decoder must NOT also
        // emit a SplitterPrimitive for that target — otherwise the renderer would
        // draw two parallel structures from the same struct signal.
        StructTypeDecl ctrl = SampleStruct(("lo", 1));
        ContAssignAst ca = new(new VarRefLValue("sliced"),
            new BitSelectExpr(new SignalRef("ctrl"), new BitRange(0, 0)));

        SchematicPrimitiveList result = SchematicDecoder.Decode(MakeModule(ctrl, [ca]));

        Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        Assert.Empty(result.Logic.OfType<SplitterPrimitive>());
    }
}
