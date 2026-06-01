using System.IO;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

/// <summary>
/// Phase 2 P2-11: <see cref="VerilatorXmlAstReader"/> recognises &lt;structdtype&gt;
/// entries in the &lt;typetable&gt; and attaches the resolved <see cref="StructTypeDecl"/>
/// to any signal whose dtype id matches. Refdtype aliases are followed.
/// PortConnectionDecl.SignalRange picks up the bit-slice from sel-wrapped pin connections.
/// </summary>
public sealed class StructTypeReaderTests
{
    // Helper: parse a full netlist (need typetable at netlist scope, so AstReaderTestHelper
    // — which wraps in a single module — isn't enough on its own).
    private static DesignAst ParseNetlist(string body)
    {
        string xml = $"""
            <?xml version="1.0"?>
            <verilator_xml>
              <netlist>
                {body}
              </netlist>
            </verilator_xml>
            """;
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

    // ── Struct type recovery ─────────────────────────────────────────────

    [Fact]
    public void StructDtype_WithThreeFields_AttachesStructType_FieldsInDeclarationOrder()
    {
        DesignAst ast = ParseNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
            </module>
            <typetable>
              <structdtype id="10" name="pkg::ctrl_t">
                <memberdtype id="20" name="hi"  sub_dtype_id="100"/>
                <memberdtype id="21" name="mid" sub_dtype_id="101"/>
                <memberdtype id="22" name="lo"  sub_dtype_id="102"/>
              </structdtype>
              <basicdtype id="100" name="logic" left="3" right="0"/>
              <basicdtype id="101" name="logic" left="1" right="0"/>
              <basicdtype id="102" name="logic"/>
            </typetable>
            """);

        SignalDecl signal = Assert.Single(ast.TopModule!.LocalSignals);
        Assert.NotNull(signal.StructType);
        Assert.Equal("pkg::ctrl_t", signal.StructType!.Name);

        // Declaration order is MSB-first (Verilog packed struct convention).
        Assert.Equal(new[] { "hi", "mid", "lo" }, signal.StructType.Fields.Select(f => f.FieldName));

        // Total width = 4 + 2 + 1 = 7
        Assert.Equal(7, signal.StructType.TotalWidth);
    }

    [Fact]
    public void StructFields_BitOffsetsAreLsbFirst_LastDeclaredAtLoZero()
    {
        DesignAst ast = ParseNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
            </module>
            <typetable>
              <structdtype id="10" name="ctrl_t">
                <memberdtype id="20" name="hi"  sub_dtype_id="100"/>
                <memberdtype id="21" name="mid" sub_dtype_id="101"/>
                <memberdtype id="22" name="lo"  sub_dtype_id="102"/>
              </structdtype>
              <basicdtype id="100" name="logic" left="3" right="0"/>
              <basicdtype id="101" name="logic" left="1" right="0"/>
              <basicdtype id="102" name="logic"/>
            </typetable>
            """);

        StructTypeDecl t = ast.TopModule!.LocalSignals[0].StructType!;
        // hi: bits [6:3], mid: bits [2:1], lo: bit [0]
        Assert.Equal((6, 3), (t.Fields[0].Hi, t.Fields[0].Lo));
        Assert.Equal((2, 1), (t.Fields[1].Hi, t.Fields[1].Lo));
        Assert.Equal((0, 0), (t.Fields[2].Hi, t.Fields[2].Lo));
    }

    [Fact]
    public void StructSignalWidth_ComesFromStructTotal_NotBasicDtype()
    {
        DesignAst ast = ParseNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="10" vartype="ctrl_t"/>
            </module>
            <typetable>
              <structdtype id="10" name="ctrl_t">
                <memberdtype id="20" name="a" sub_dtype_id="100"/>
                <memberdtype id="21" name="b" sub_dtype_id="100"/>
              </structdtype>
              <basicdtype id="100" name="logic" left="3" right="0"/>
            </typetable>
            """);

        SignalDecl signal = Assert.Single(ast.TopModule!.LocalSignals);
        Assert.Equal(8, signal.Width);   // 4 + 4
    }

    [Fact]
    public void RefDtype_AliasResolvesToStructType()
    {
        DesignAst ast = ParseNetlist("""
            <module name="top" topModule="1">
              <var name="ctrl" dtype_id="58" vartype="ctrl_t"/>
            </module>
            <typetable>
              <structdtype id="10" name="ctrl_t">
                <memberdtype id="20" name="only_field" sub_dtype_id="100"/>
              </structdtype>
              <refdtype id="58" name="ctrl_t" sub_dtype_id="10"/>
              <basicdtype id="100" name="logic"/>
            </typetable>
            """);

        Assert.NotNull(ast.TopModule!.LocalSignals[0].StructType);
        Assert.Equal("ctrl_t", ast.TopModule!.LocalSignals[0].StructType!.Name);
    }

    [Fact]
    public void SignalWithoutStructDtype_HasNullStructType()
    {
        DesignAst ast = ParseNetlist("""
            <module name="top" topModule="1">
              <var name="plain" dtype_id="100"/>
            </module>
            <typetable>
              <basicdtype id="100" name="logic" left="7" right="0"/>
            </typetable>
            """);

        Assert.Null(ast.TopModule!.LocalSignals[0].StructType);
    }

    // ── PortConnectionDecl.SignalRange ───────────────────────────────────

    [Fact]
    public void PortConnection_WithSelWrapper_PopulatesSignalRange()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="control_pins" dtype_id="2"/>
            <instance name="alu_i" defName="alu">
              <port name="ops" direction="in" portIndex="1">
                <sel>
                  <varref name="control_pins"/>
                  <const name="32'h2"/>
                  <const name="32'h2"/>
                </sel>
              </port>
            </instance>
            """);

        PortConnectionDecl conn = Assert.Single(ast.TopModule!.Instances[0].PortConnections);
        Assert.Equal("control_pins", conn.SignalName);
        Assert.NotNull(conn.SignalRange);
        Assert.Equal(3, conn.SignalRange!.Value.Hi);
        Assert.Equal(2, conn.SignalRange.Value.Lo);
    }

    [Fact]
    public void PortConnection_WithoutSelWrapper_SignalRangeIsNull()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="x" dtype_id="1"/>
            <instance name="sub" defName="leaf">
              <port name="a" direction="in" portIndex="1">
                <varref name="x"/>
              </port>
            </instance>
            """);

        PortConnectionDecl conn = Assert.Single(ast.TopModule!.Instances[0].PortConnections);
        Assert.Null(conn.SignalRange);
    }

    [Fact]
    public void PortConnection_SelWidthOne_RangeHiEqualsLo()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="ctrl" dtype_id="1"/>
            <instance name="sub" defName="leaf">
              <port name="bit" direction="in" portIndex="1">
                <sel>
                  <varref name="ctrl"/>
                  <const name="32'h5"/>
                  <const name="32'h1"/>
                </sel>
              </port>
            </instance>
            """);

        PortConnectionDecl conn = Assert.Single(ast.TopModule!.Instances[0].PortConnections);
        Assert.Equal(5, conn.SignalRange!.Value.Hi);
        Assert.Equal(5, conn.SignalRange.Value.Lo);
    }
}
