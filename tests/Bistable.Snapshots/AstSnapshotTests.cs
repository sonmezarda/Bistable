using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Snapshots;

/// <summary>
/// Golden-file snapshot tests for <see cref="DesignAst"/>.
/// Each test parses a synthetic XML and captures the resulting AST as JSON.
/// Run with BISTABLE_REGENERATE_SNAPSHOTS=1 to accept new snapshots.
/// </summary>
[Trait("Category", "Snapshot")]
public sealed class AstSnapshotTests
{
    private static readonly JsonSerializerOptions AstSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    // ── Synthetic: arnicomp always-block pattern ─────────────────────────────

    [Fact]
    public void AstSnapshot_ArnicompAlwaysPattern_MatchesGolden()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="arnicomp_top" topModule="1">
                  <var name="clk"         dtype_id="1" dir="input"  pinIndex="1" vartype="logic"/>
                  <var name="rst_n"        dtype_id="1" dir="input"  pinIndex="2" vartype="logic"/>
                  <var name="instruction"  dtype_id="8" dir="input"  pinIndex="3" vartype="logic"/>
                  <var name="inst_q"       dtype_id="8" vartype="logic"/>
                  <always>
                    <sentree>
                      <senitem edgeType="POS"><varref name="clk"/></senitem>
                      <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
                    </sentree>
                    <begin>
                      <assigndly dtype_id="8">
                        <cond>
                          <varref name="rst_n"/>
                          <varref name="instruction"/>
                          <const name="8'h0"/>
                        </cond>
                        <varref name="inst_q"/>
                      </assigndly>
                    </begin>
                  </always>
                </module>
              </netlist>
            </verilator_xml>
            """;

        DesignAst ast = ParseXml(xml);
        SnapshotAssert.MatchesJson("ast-arnicomp-always-pattern", ast, AstSerializerOptions);
    }

    // ── Synthetic: concat + sel + cond contassigns ───────────────────────────

    [Fact]
    public void AstSnapshot_ContAssignVariants_MatchesGolden()
    {
        const string xml = """
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                <module name="top" topModule="1">
                  <var name="bus"    dtype_id="8"  dir="input"  pinIndex="1" vartype="logic"/>
                  <var name="sel"    dtype_id="1"  dir="input"  pinIndex="2" vartype="logic"/>
                  <var name="a"      dtype_id="8"  dir="input"  pinIndex="3" vartype="logic"/>
                  <var name="b"      dtype_id="8"  dir="input"  pinIndex="4" vartype="logic"/>
                  <var name="hi"     dtype_id="4"  dir="input"  pinIndex="5" vartype="logic"/>
                  <var name="lo"     dtype_id="4"  dir="input"  pinIndex="6" vartype="logic"/>
                  <var name="result" dtype_id="8"  dir="output" pinIndex="7" vartype="logic"/>
                  <var name="sliced" dtype_id="2"  dir="output" pinIndex="8" vartype="logic"/>
                  <var name="joined" dtype_id="8"  dir="output" pinIndex="9" vartype="logic"/>
                  <!-- ternary mux -->
                  <contassign dtype_id="8">
                    <cond dtype_id="8">
                      <varref name="sel"/>
                      <varref name="a"/>
                      <varref name="b"/>
                    </cond>
                    <varref name="result"/>
                  </contassign>
                  <!-- bit-range select -->
                  <contassign dtype_id="2">
                    <sel dtype_id="2">
                      <varref name="bus"/>
                      <const name="32'h4"/>
                      <const name="32'h2"/>
                    </sel>
                    <varref name="sliced"/>
                  </contassign>
                  <!-- concat joiner -->
                  <contassign dtype_id="8">
                    <concat dtype_id="8">
                      <varref name="hi"/>
                      <varref name="lo"/>
                    </concat>
                    <varref name="joined"/>
                  </contassign>
                </module>
              </netlist>
            </verilator_xml>
            """;

        DesignAst ast = ParseXml(xml);
        SnapshotAssert.MatchesJson("ast-contassign-variants", ast, AstSerializerOptions);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DesignAst ParseXml(string xml)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            return new VerilatorXmlAstReader().Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
