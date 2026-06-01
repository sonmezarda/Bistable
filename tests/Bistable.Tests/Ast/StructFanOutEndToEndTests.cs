using System.IO;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

/// <summary>
/// Phase 2 P2-11 end-to-end: XML → AST (with struct types) → Decode produces a
/// StructFanOutPrimitive with the right field labels and consumer wiring. This
/// guards the whole pipeline against subtle drift between the reader, decoder,
/// and the struct-type metadata.
/// </summary>
public sealed class StructFanOutEndToEndTests
{
    private static SchematicPrimitiveList DecodeNetlist(string netlistBody)
    {
        string xml = $"""
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                {netlistBody}
              </netlist>
            </verilator_xml>
            """;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            DesignAst ast = new VerilatorXmlAstReader().Read(path);
            return SchematicDecoder.Decode(ast.TopModule!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── End-to-end: arnicomp-style control_pins pattern ───────────────────

    [Fact]
    public void EndToEnd_ControlStructWithMultipleConsumers_RendersOneFanOutWithThreeLegs()
    {
        // Mimics arnicomp_top: a struct signal "ctrl" with three consumers — two
        // instance pins (.ops, .we) and one local contassign (acc_we_alias).
        SchematicPrimitiveList result = DecodeNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
              <var name="acc_we_alias" dtype_id="100"/>
              <contassign dtype_id="100">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h0"/>
                  <const name="32'h1"/>
                </sel>
                <varref name="acc_we_alias"/>
              </contassign>
              <instance name="alu_i" defName="alu">
                <port name="ops" direction="in" portIndex="1">
                  <sel>
                    <varref name="ctrl"/>
                    <const name="32'h1"/>
                    <const name="32'h2"/>
                  </sel>
                </port>
                <port name="we" direction="in" portIndex="2">
                  <sel>
                    <varref name="ctrl"/>
                    <const name="32'h0"/>
                    <const name="32'h1"/>
                  </sel>
                </port>
              </instance>
            </module>
            <typetable>
              <structdtype id="10" name="pkg::ctrl_t">
                <memberdtype id="20" name="ops" sub_dtype_id="200"/>
                <memberdtype id="21" name="we"  sub_dtype_id="100"/>
              </structdtype>
              <basicdtype id="200" name="logic" left="1" right="0"/>
              <basicdtype id="100" name="logic"/>
            </typetable>
            """);

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        Assert.Equal("ctrl", fanOut.StructSignal);
        Assert.Equal("pkg::ctrl_t", fanOut.StructTypeName);
        Assert.Equal(3, fanOut.StructWidth);

        // 2 distinct slices: ops (bits 2..1) and we (bit 0)
        Assert.Equal(2, fanOut.Legs.Count);

        StructFanOutLeg opsLeg = Assert.Single(fanOut.Legs, l => l.FieldName == "ops");
        Assert.Equal(new BitRange(2, 1), opsLeg.Range);
        Assert.Equal(new[] { "alu_i.ops" }, opsLeg.Consumers);

        StructFanOutLeg weLeg = Assert.Single(fanOut.Legs, l => l.FieldName == "we");
        Assert.Equal(new BitRange(0, 0), weLeg.Range);
        Assert.Equal(2, weLeg.Consumers.Count);
        Assert.Contains("alu_i.we", weLeg.Consumers);
        Assert.Contains("acc_we_alias", weLeg.Consumers);
    }

    [Fact]
    public void EndToEnd_LegsAreSortedByDescendingHi()
    {
        // Three single-bit fields, consumed in random order — the fan-out legs must
        // come out MSB-first so the rendered wedge stacks legs in a predictable order.
        SchematicPrimitiveList result = DecodeNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
              <var name="t_lo" dtype_id="100"/>
              <var name="t_mid" dtype_id="100"/>
              <var name="t_hi" dtype_id="100"/>
              <contassign dtype_id="100">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h0"/>
                  <const name="32'h1"/>
                </sel>
                <varref name="t_lo"/>
              </contassign>
              <contassign dtype_id="100">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h2"/>
                  <const name="32'h1"/>
                </sel>
                <varref name="t_hi"/>
              </contassign>
              <contassign dtype_id="100">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h1"/>
                  <const name="32'h1"/>
                </sel>
                <varref name="t_mid"/>
              </contassign>
            </module>
            <typetable>
              <structdtype id="10" name="ctrl_t">
                <memberdtype id="20" name="hi"  sub_dtype_id="100"/>
                <memberdtype id="21" name="mid" sub_dtype_id="100"/>
                <memberdtype id="22" name="lo"  sub_dtype_id="100"/>
              </structdtype>
              <basicdtype id="100" name="logic"/>
            </typetable>
            """);

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        Assert.Equal(new[] { "hi", "mid", "lo" }, fanOut.Legs.Select(l => l.FieldName));
    }

    [Fact]
    public void EndToEnd_StructWithRefDtype_StillProducesFanOut()
    {
        // Arnicomp uses <refdtype> aliases; the reader must follow them so the
        // signal still picks up the underlying StructTypeDecl.
        SchematicPrimitiveList result = DecodeNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="58" vartype="ctrl_t"/>
              <var name="t" dtype_id="100"/>
              <contassign dtype_id="100">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h0"/>
                  <const name="32'h1"/>
                </sel>
                <varref name="t"/>
              </contassign>
            </module>
            <typetable>
              <structdtype id="10" name="ctrl_t">
                <memberdtype id="20" name="bit0" sub_dtype_id="100"/>
              </structdtype>
              <refdtype id="58" sub_dtype_id="10"/>
              <basicdtype id="100" name="logic"/>
            </typetable>
            """);

        StructFanOutPrimitive fanOut = Assert.Single(result.Logic.OfType<StructFanOutPrimitive>());
        Assert.Equal("bit0", Assert.Single(fanOut.Legs).FieldName);
    }
}
