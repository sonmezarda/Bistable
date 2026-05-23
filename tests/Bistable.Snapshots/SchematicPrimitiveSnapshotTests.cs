using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Schematic;
using Bistable.Verilator;

namespace Bistable.Snapshots;

/// <summary>
/// Golden-file snapshots for <see cref="SchematicPrimitiveList"/>.
/// Validates that AST → primitive decoding remains stable across refactors.
/// Run with BISTABLE_REGENERATE_SNAPSHOTS=1 to accept new snapshots.
/// </summary>
[Trait("Category", "Snapshot")]
public sealed class SchematicPrimitiveSnapshotTests
{
    private static readonly JsonSerializerOptions PrimitiveSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void PrimitiveSnapshot_ArnicompRegisterCellPattern_MatchesGolden()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="reg_cell" topModule="1">
                  <var name="clk"    dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
                  <var name="rst_n"  dtype_id="1" dir="input"  pinIndex="2" vartype="logic"/>
                  <var name="d_in"   dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
                  <var name="d_out"  dtype_id="8" dir="output" pinIndex="4" vartype="logic"/>
                  <var name="q"      dtype_id="8" vartype="logic"/>
                  <always>
                    <sentree>
                      <senitem edgeType="POS"><varref name="clk"/></senitem>
                      <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
                    </sentree>
                    <assigndly dtype_id="8">
                      <cond>
                        <varref name="rst_n"/>
                        <varref name="d_in"/>
                        <const name="8'h0"/>
                      </cond>
                      <varref name="q"/>
                    </assigndly>
                  </always>
                  <contassign dtype_id="8">
                    <varref name="q"/>
                    <varref name="d_out"/>
                  </contassign>
                </module>
              </netlist>
            </verilator_xml>
            """;

        SchematicPrimitiveList primitives = DecodeXml(xml);
        SnapshotAssert.MatchesJson("primitives-reg-cell", primitives, PrimitiveSerializerOptions);
    }

    [Fact]
    public void PrimitiveSnapshot_MuxAndArithMix_MatchesGolden()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="alu_lite" topModule="1">
                  <var name="op_sel" dtype_id="2" dir="input"  pinIndex="1" vartype="logic"/>
                  <var name="a"      dtype_id="8" dir="input"  pinIndex="2" vartype="logic"/>
                  <var name="b"      dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
                  <var name="result" dtype_id="8" dir="output" pinIndex="4" vartype="logic"/>
                  <var name="sum"    dtype_id="8" vartype="logic"/>
                  <var name="diff"   dtype_id="8" vartype="logic"/>
                  <var name="and_v"  dtype_id="8" vartype="logic"/>
                  <contassign dtype_id="8">
                    <add dtype_id="8"><varref name="a"/><varref name="b"/></add>
                    <varref name="sum"/>
                  </contassign>
                  <contassign dtype_id="8">
                    <sub dtype_id="8"><varref name="a"/><varref name="b"/></sub>
                    <varref name="diff"/>
                  </contassign>
                  <contassign dtype_id="8">
                    <and dtype_id="8"><varref name="a"/><varref name="b"/></and>
                    <varref name="and_v"/>
                  </contassign>
                  <contassign dtype_id="8">
                    <cond dtype_id="8">
                      <varref name="op_sel"/>
                      <varref name="sum"/>
                      <cond dtype_id="8">
                        <varref name="op_sel"/>
                        <varref name="diff"/>
                        <varref name="and_v"/>
                      </cond>
                    </cond>
                    <varref name="result"/>
                  </contassign>
                </module>
              </netlist>
            </verilator_xml>
            """;

        SchematicPrimitiveList primitives = DecodeXml(xml);
        SnapshotAssert.MatchesJson("primitives-alu-lite", primitives, PrimitiveSerializerOptions);
    }

    private static SchematicPrimitiveList DecodeXml(string xml)
    {
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
}
