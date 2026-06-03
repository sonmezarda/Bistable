using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Tests.Ast;

namespace Bistable.Tests.Schematic;

public sealed class SchematicDecoderTests
{
    private static ModuleAst DecodeFirstModule(string moduleBody)
    {
        DesignAst ast = AstReaderTestHelper.ParseInline(moduleBody);
        return ast.TopModule!;
    }

    // ── BufferPrimitive ──────────────────────────────────────────────────────

    [Fact]
    public void Buffer_SimpleWireAlias_EmitsBufferPrimitive()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="1">
              <varref name="src"/>
              <varref name="dst"/>
            </contassign>
            """));

        BufferPrimitive buf = Assert.Single(result.Logic.OfType<BufferPrimitive>());
        Assert.Equal("dst", buf.OutputSignal);
        Assert.Equal("src", buf.InputSignal);
    }

    // ── SplitterPrimitive ────────────────────────────────────────────────────

    [Fact]
    public void Splitter_BitSelect_EmitsSplitterWithRange()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="2">
              <sel dtype_id="2">
                <varref name="bus"/>
                <const name="32'h6"/>
                <const name="32'h2"/>
              </sel>
              <varref name="slice"/>
            </contassign>
            """));

        SplitterPrimitive split = Assert.Single(result.Logic.OfType<SplitterPrimitive>());
        Assert.Equal("slice", split.OutputSignal);
        Assert.Equal("bus",   split.InputSignal);
        Assert.Equal(7, split.Range.Hi);
        Assert.Equal(6, split.Range.Lo);
    }

    // ── JoinerPrimitive ──────────────────────────────────────────────────────

    [Fact]
    public void Joiner_Concat_EmitsJoinerWithMsbOrder()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="16">
              <concat dtype_id="16">
                <varref name="hi"/>
                <varref name="lo"/>
              </concat>
              <varref name="result"/>
            </contassign>
            """));

        JoinerPrimitive join = Assert.Single(result.Logic.OfType<JoinerPrimitive>());
        Assert.Equal("result", join.OutputSignal);
        Assert.Equal(new[] { "hi", "lo" }, join.InputSignals);
    }

    [Fact]
    public void Joiner_ReplicateSignal_EmitsJoinerWithRepeatedPattern()
    {
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports:
            [
                new PortDecl("a", SignalDirection.Input, 1, false, 0),
                new PortDecl("y", SignalDirection.Output, 4, false, 1)
            ],
            Parameters: [],
            LocalSignals: [],
            Instances: [],
            ContAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("y"),
                    new ReplicateExpr(4, new SignalRef("a")))
            ],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList result = SchematicDecoder.Decode(module);

        JoinerPrimitive join = Assert.Single(result.Logic.OfType<JoinerPrimitive>());
        Assert.Equal("y", join.OutputSignal);
        Assert.Equal(new[] { "a", "a", "a", "a" }, join.InputSignals);
    }

    [Fact]
    public void ConstantTie_ReplicateConstant_FoldsToSingleLiteral()
    {
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports: [new PortDecl("y", SignalDirection.Output, 4, false, 0)],
            Parameters: [],
            LocalSignals: [],
            Instances: [],
            ContAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("y"),
                    new ReplicateExpr(4, new ConstExpr(System.Numerics.BigInteger.One, 1, false)))
            ],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList result = SchematicDecoder.Decode(module);

        ConstantTiePrimitive tie = Assert.Single(result.Logic.OfType<ConstantTiePrimitive>());
        Assert.Equal("y", tie.OutputSignal);
        Assert.Equal("4'hF", tie.Literal);
        Assert.Equal(4, tie.Width);
    }

    // ── InverterPrimitive ────────────────────────────────────────────────────

    [Fact]
    public void Inverter_UnaryNot_EmitsInverter()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="1">
              <not dtype_id="1">
                <varref name="x"/>
              </not>
              <varref name="y"/>
            </contassign>
            """));

        InverterPrimitive inv = Assert.Single(result.Logic.OfType<InverterPrimitive>());
        Assert.Equal("y", inv.OutputSignal);
        Assert.Equal("x", inv.InputSignal);
    }

    // ── GatePrimitive ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("and", GateKind.And)]
    [InlineData("or",  GateKind.Or)]
    [InlineData("xor", GateKind.Xor)]
    public void Gate_BinaryLogic_EmitsGatePrimitive(string xmlTag, GateKind expectedKind)
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule($"""
            <contassign dtype_id="1">
              <{xmlTag} dtype_id="1">
                <varref name="a"/>
                <varref name="b"/>
              </{xmlTag}>
              <varref name="y"/>
            </contassign>
            """));

        GatePrimitive gate = Assert.Single(result.Logic.OfType<GatePrimitive>());
        Assert.Equal(expectedKind, gate.Kind);
        Assert.Equal("y", gate.OutputSignal);
        Assert.Equal(new[] { "a", "b" }, gate.InputSignals);
    }

    [Theory]
    [InlineData("redand", GateKind.ReduceAnd)]
    [InlineData("redor",  GateKind.ReduceOr)]
    [InlineData("redxor", GateKind.ReduceXor)]
    public void Gate_ReductionOps_EmitsGatePrimitive(string xmlTag, GateKind expectedKind)
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule($"""
            <contassign dtype_id="1">
              <{xmlTag} dtype_id="1">
                <varref name="bus"/>
              </{xmlTag}>
              <varref name="y"/>
            </contassign>
            """));

        GatePrimitive gate = Assert.Single(result.Logic.OfType<GatePrimitive>());
        Assert.Equal(expectedKind, gate.Kind);
        Assert.Equal(new[] { "bus" }, gate.InputSignals);
    }

    // ── ArithPrimitive ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("add", ArithKind.Add)]
    [InlineData("sub", ArithKind.Sub)]
    [InlineData("eq",  ArithKind.Equal)]
    [InlineData("lt",  ArithKind.LessThan)]
    public void Arith_BinaryOp_EmitsArithPrimitive(string xmlTag, ArithKind expectedKind)
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule($"""
            <contassign dtype_id="8">
              <{xmlTag} dtype_id="8">
                <varref name="a"/>
                <varref name="b"/>
              </{xmlTag}>
              <varref name="y"/>
            </contassign>
            """));

        ArithPrimitive arith = Assert.Single(result.Logic.OfType<ArithPrimitive>());
        Assert.Equal(expectedKind, arith.Kind);
        Assert.Equal("y", arith.OutputSignal);
        Assert.Equal("a", arith.LeftSignal);
        Assert.Equal("b", arith.RightSignal);
    }

    // ── MuxPrimitive ─────────────────────────────────────────────────────────

    [Fact]
    public void Mux_SimpleTernary_EmitsTwoInputMux()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="8">
              <cond dtype_id="8">
                <varref name="sel"/>
                <varref name="a"/>
                <varref name="b"/>
              </cond>
              <varref name="out"/>
            </contassign>
            """));

        MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
        Assert.Equal("out", mux.OutputSignal);
        Assert.Equal(new[] { "sel" }, mux.SelectSignals);
        Assert.Equal(2, mux.Inputs.Count);
        Assert.Equal("a", ((MuxSignalSource)mux.Inputs[0].Source).SignalName);
        Assert.Equal("b", ((MuxSignalSource)mux.Inputs[1].Source).SignalName);
    }

    [Fact]
    public void Mux_NestedCond_FlattensToThreeInputs()
    {
        // sel1 ? a : (sel0 ? b : c)
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="8">
              <cond dtype_id="8">
                <varref name="sel1"/>
                <varref name="a"/>
                <cond dtype_id="8">
                  <varref name="sel0"/>
                  <varref name="b"/>
                  <varref name="c"/>
                </cond>
              </cond>
              <varref name="out"/>
            </contassign>
            """));

        MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
        Assert.Equal(3, mux.Inputs.Count);
        Assert.Equal("a", ((MuxSignalSource)mux.Inputs[0].Source).SignalName);
        Assert.Equal("b", ((MuxSignalSource)mux.Inputs[1].Source).SignalName);
        Assert.Equal("c", ((MuxSignalSource)mux.Inputs[2].Source).SignalName);
    }

    [Fact]
    public void Mux_ConstantInBranch_EmitsConstSource()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="8">
              <cond dtype_id="8">
                <varref name="sel"/>
                <varref name="a"/>
                <const name="8'h0"/>
              </cond>
              <varref name="out"/>
            </contassign>
            """));

        MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
        Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);
        MuxConstantSource constSrc = Assert.IsType<MuxConstantSource>(mux.Inputs[1].Source);
        Assert.Equal("0", constSrc.Literal);
        Assert.Equal(8, constSrc.Width);
    }

    [Fact]
    public void Mux_ComplexBranch_MaterializesIntermediateGate()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="1">
              <cond dtype_id="1">
                <varref name="sel"/>
                <and dtype_id="1">
                  <varref name="a"/>
                  <varref name="b"/>
                </and>
                <varref name="c"/>
              </cond>
              <varref name="out"/>
            </contassign>
            """));

        MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
        GatePrimitive gate = Assert.Single(result.Logic.OfType<GatePrimitive>());
        MuxSignalSource branchSource = Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);

        Assert.Equal(gate.OutputSignal, branchSource.SignalName);
        Assert.Equal(new[] { "a", "b" }, gate.InputSignals);
        Assert.Equal("1", mux.Inputs[0].Label);
    }

    [Fact]
    public void Arith_ComplexOperand_MaterializesIntermediateGate()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <contassign dtype_id="8">
              <add dtype_id="8">
                <and dtype_id="1">
                  <varref name="a"/>
                  <varref name="b"/>
                </and>
                <varref name="c"/>
              </add>
              <varref name="sum"/>
            </contassign>
            """));

        ArithPrimitive arith = Assert.Single(result.Logic.OfType<ArithPrimitive>());
        GatePrimitive gate = Assert.Single(result.Logic.OfType<GatePrimitive>());

        Assert.Equal(gate.OutputSignal, arith.LeftSignal);
        Assert.Equal("c", arith.RightSignal);
        Assert.Equal(new[] { "a", "b" }, gate.InputSignals);
    }

    [Fact]
    public void MemoryRead_ArraySelect_EmitsMemoryReadPrimitive()
    {
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports:
            [
                new PortDecl("addr", SignalDirection.Input, 4, false, 0),
                new PortDecl("data", SignalDirection.Output, 8, false, 1)
            ],
            Parameters: [],
            LocalSignals: [new SignalDecl("mem", 8, false, [new BitRange(15, 0)])],
            Instances: [],
            ContAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("data"),
                    new ArraySelectExpr(new SignalRef("mem"), new SignalRef("addr")))
            ],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList result = SchematicDecoder.Decode(module);

        MemoryReadPrimitive read = Assert.Single(result.Logic.OfType<MemoryReadPrimitive>());
        Assert.Equal("mem", read.MemorySignal);
        Assert.Equal("addr", read.AddressSignal);
        Assert.Equal("data", read.OutputSignal);
        Assert.Equal(8, read.CellWidth);
    }

    [Fact]
    public void Mux_ArraySelectBranch_MaterializesIntermediateMemoryRead()
    {
        ModuleAst module = new(
            Name: "top",
            IsTop: true,
            Ports:
            [
                new PortDecl("sel", SignalDirection.Input, 1, false, 0),
                new PortDecl("addr", SignalDirection.Input, 4, false, 1),
                new PortDecl("fallback", SignalDirection.Input, 8, false, 2),
                new PortDecl("data", SignalDirection.Output, 8, false, 3)
            ],
            Parameters: [],
            LocalSignals: [new SignalDecl("mem", 8, false, [new BitRange(15, 0)])],
            Instances: [],
            ContAssigns:
            [
                new ContAssignAst(
                    new VarRefLValue("data"),
                    new CondExpr(
                        new SignalRef("sel"),
                        new ArraySelectExpr(new SignalRef("mem"), new SignalRef("addr")),
                        new SignalRef("fallback")))
            ],
            SequentialBlocks: [],
            CombinationalBlocks: []);

        SchematicPrimitiveList result = SchematicDecoder.Decode(module);

        MuxPrimitive mux = Assert.Single(result.Logic.OfType<MuxPrimitive>());
        MemoryReadPrimitive read = Assert.Single(result.Logic.OfType<MemoryReadPrimitive>());
        MuxSignalSource source = Assert.IsType<MuxSignalSource>(mux.Inputs[0].Source);
        Assert.Equal(read.OutputSignal, source.SignalName);
        Assert.Equal("addr", read.AddressSignal);
    }

    // ── FlipFlopPrimitive ───────────────────────────────────────────────────

    [Fact]
    public void FlipFlop_BasicPosedge_EmitsFlipFlop()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <varref name="d_in"/>
                <varref name="q"/>
              </assigndly>
            </always>
            """));

        FlipFlopPrimitive ff = Assert.Single(result.Logic.OfType<FlipFlopPrimitive>());
        Assert.Equal("q",    ff.QSignal);
        Assert.Equal("clk",  ff.ClockSignal);
        Assert.Equal(EdgeKind.Rising, ff.ClockEdge);
        Assert.Equal("d_in", ff.DSignal);
        Assert.Null(ff.AsyncResetSignal);
    }

    [Fact]
    public void FlipFlop_WithAsyncReset_PeelsResetMux()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
                <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <cond>
                  <varref name="rst_n"/>
                  <varref name="instruction"/>
                  <const name="8'h0"/>
                </cond>
                <varref name="inst_q"/>
              </assigndly>
            </always>
            """));

        FlipFlopPrimitive ff = Assert.Single(result.Logic.OfType<FlipFlopPrimitive>());
        Assert.Equal("inst_q", ff.QSignal);
        Assert.Equal("clk",    ff.ClockSignal);
        Assert.Equal("rst_n",  ff.AsyncResetSignal);
        Assert.Equal(EdgeKind.Falling, ff.AsyncResetEdge);
        // After peeling the reset mux, D should be "instruction", not a CondExpr
        Assert.Equal("instruction", ff.DSignal);
    }

    [Fact]
    public void FlipFlop_BodyInsideBegin_StillDetected()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <assigndly dtype_id="8">
                  <varref name="d_in"/>
                  <varref name="q"/>
                </assigndly>
              </begin>
            </always>
            """));

        FlipFlopPrimitive ff = Assert.Single(result.Logic.OfType<FlipFlopPrimitive>());
        Assert.Equal("q", ff.QSignal);
    }

    [Fact]
    public void FlipFlop_ComplexDInput_MaterializesIntermediateGate()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="1">
                <and dtype_id="1">
                  <varref name="a"/>
                  <varref name="b"/>
                </and>
                <varref name="q"/>
              </assigndly>
            </always>
            """));

        FlipFlopPrimitive ff = Assert.Single(result.Logic.OfType<FlipFlopPrimitive>());
        GatePrimitive gate = Assert.Single(result.Logic.OfType<GatePrimitive>());

        Assert.Equal(gate.OutputSignal, ff.DSignal);
        Assert.Equal(new[] { "a", "b" }, gate.InputSignals);
    }

    // ── MemoryPrimitive ──────────────────────────────────────────────────────

    [Fact]
    public void Memory_UnpackedArray_EmitsMemoryPrimitive()
    {
        // The reader resolves array dimensions from <unpackarraydtype>, which we mock here.
        // Direct path: parse a manually-constructed XML where the dtype is an unpackarraydtype.
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <unpackarraydtype id="arr8x16" left="15" right="0"/>
                <module name="top" topModule="1">
                  <var name="mem" dtype_id="arr8x16" vartype="logic"/>
                </module>
              </netlist>
            </verilator_xml>
            """;

        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, xml);
            DesignAst ast = new Bistable.Verilator.VerilatorXmlAstReader().Read(path);
            SchematicPrimitiveList result = SchematicDecoder.Decode(ast.TopModule!);

            MemoryPrimitive mem = Assert.Single(result.Logic.OfType<MemoryPrimitive>());
            Assert.Equal("mem", mem.SignalName);
            Assert.Equal(15, mem.DepthHi);
            Assert.Equal(0,  mem.DepthLo);
            Assert.Equal(16, mem.Depth);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    // ── InstancePrimitive ────────────────────────────────────────────────────

    [Fact]
    public void Instance_PortConnections_PreservedInBinding()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <instance name="alu_i" defName="alu" origName="alu_i">
              <port name="clk" direction="in"  portIndex="1"><varref name="clk"/></port>
              <port name="out" direction="out" portIndex="2"><varref name="result"/></port>
            </instance>
            """));

        InstancePrimitive inst = Assert.Single(result.Instances);
        Assert.Equal("alu_i", inst.InstanceName);
        Assert.Equal("alu",   inst.ModuleName);
        Assert.Equal(2, inst.Pins.Count);
        Assert.Equal("clk",    inst.Pins[0].SignalName);
        Assert.Equal("result", inst.Pins[1].SignalName);
    }

    // ── PortPrimitive ────────────────────────────────────────────────────────

    [Fact]
    public void Port_TopModulePorts_EmittedAsPortPrimitives()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <var name="clk"   dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="dout"  dtype_id="8" dir="output" pinIndex="2" vartype="logic"/>
            """));

        Assert.Equal(2, result.Ports.Count);
        Assert.Equal("clk",  result.Ports[0].Name);
        Assert.Equal(SignalDirection.Input, result.Ports[0].Direction);
        Assert.Equal("dout", result.Ports[1].Name);
    }

    // ── SignalPrimitive ──────────────────────────────────────────────────────

    [Fact]
    public void Signal_LocalSignal_EmittedWithIsRegisteredFlag()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <var name="q"   dtype_id="8" vartype="logic"/>
            <var name="w"   dtype_id="8" vartype="logic"/>
            <contassign dtype_id="8">
              <varref name="src"/>
              <varref name="w"/>
            </contassign>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <varref name="d_in"/>
                <varref name="q"/>
              </assigndly>
            </always>
            """));

        SignalPrimitive q = Assert.Single(result.Signals, s => s.Name == "q");
        SignalPrimitive w = Assert.Single(result.Signals, s => s.Name == "w");
        Assert.True(q.IsRegistered);
        Assert.False(w.IsRegistered);
    }

    // ── Module name preserved ────────────────────────────────────────────────

    [Fact]
    public void Decode_ResultCarriesModuleName()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule(""));
        Assert.Equal("top", result.ModuleName);
    }

    // ── Mixed module: real arnicomp-like example ─────────────────────────────

    [Fact]
    public void Decode_ArnicompPattern_EmitsFlipFlopAndContAssigns()
    {
        SchematicPrimitiveList result = SchematicDecoder.Decode(DecodeFirstModule("""
            <var name="clk"          dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
            <var name="rst_n"        dtype_id="1" dir="input"  pinIndex="2" vartype="logic"/>
            <var name="instruction"  dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
            <var name="acc_in"       dtype_id="8" dir="input"  pinIndex="4" vartype="logic"/>
            <var name="acc_out"      dtype_id="8" dir="output" pinIndex="5" vartype="logic"/>
            <var name="acc_q"        dtype_id="8" vartype="logic"/>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
                <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <cond>
                  <varref name="rst_n"/>
                  <varref name="acc_in"/>
                  <const name="8'h0"/>
                </cond>
                <varref name="acc_q"/>
              </assigndly>
            </always>
            <contassign dtype_id="8">
              <varref name="acc_q"/>
              <varref name="acc_out"/>
            </contassign>
            """));

        // One FF, one buffer (acc_out wire alias)
        FlipFlopPrimitive ff = Assert.Single(result.Logic.OfType<FlipFlopPrimitive>());
        BufferPrimitive buf  = Assert.Single(result.Logic.OfType<BufferPrimitive>());
        Assert.Equal("acc_q", ff.QSignal);
        Assert.Equal("rst_n", ff.AsyncResetSignal);
        Assert.Equal("acc_in", ff.DSignal);
        Assert.Equal("acc_out", buf.OutputSignal);
        Assert.Equal("acc_q",   buf.InputSignal);
    }
}
