using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

public sealed class LegacyFlattenerTests
{
    private static ElaboratedDesign Flatten(string moduleBody)
    {
        DesignAst ast = AstReaderTestHelper.ParseInline(moduleBody);
        return LegacyDesignFlattener.Flatten(ast);
    }

    // ── Module metadata ───────────────────────────────────────────────────────

    [Fact]
    public void Flatten_Port_AppearsInModuleMetadata()
    {
        ElaboratedDesign design = Flatten("""
            <var name="clk" dtype_id="1" dir="input" pinIndex="1" vartype="logic"/>
            """);

        SignalPort port = Assert.Single(design.TopModule.Ports);
        Assert.Equal("clk", port.Name);
        Assert.Equal(SignalDirection.Input, port.Direction);
    }

    [Fact]
    public void Flatten_LocalSignal_AppearsInModuleDefinition()
    {
        ElaboratedDesign design = Flatten("""
            <var name="wire_w" dtype_id="8" vartype="logic"/>
            """);

        DesignModuleDefinition def = design.ModuleDefinitions["top"];
        DesignLocalSignal local = Assert.Single(def.LocalSignals);
        Assert.Equal("wire_w", local.Name);
    }

    // ── ContAssign flattening ─────────────────────────────────────────────────

    [Fact]
    public void Flatten_SimpleWireAlias_NullOperatorSymbol()
    {
        ElaboratedDesign design = Flatten("""
            <contassign dtype_id="8">
              <varref name="src"/>
              <varref name="dst"/>
            </contassign>
            """);

        DesignContAssign ca = Assert.Single(design.ModuleDefinitions["top"].ContAssigns);
        Assert.Equal("dst", ca.TargetName);
        Assert.Equal("src", Assert.Single(ca.SourceNames));
        Assert.Null(ca.OperatorSymbol);
        Assert.Null(ca.SourceRange);
    }

    [Fact]
    public void Flatten_BitSelect_SetsSourceRange()
    {
        ElaboratedDesign design = Flatten("""
            <contassign dtype_id="2">
              <sel dtype_id="2">
                <varref name="bus"/>
                <const name="32'h6"/>
                <const name="32'h2"/>
              </sel>
              <varref name="slice_out"/>
            </contassign>
            """);

        DesignContAssign ca = Assert.Single(design.ModuleDefinitions["top"].ContAssigns);
        Assert.Equal("slice_out", ca.TargetName);
        Assert.Equal("bus", Assert.Single(ca.SourceNames));
        Assert.Null(ca.OperatorSymbol);
        Assert.NotNull(ca.SourceRange);
        Assert.Equal(7, ca.SourceRange!.Value.Hi);
        Assert.Equal(6, ca.SourceRange!.Value.Lo);
    }

    [Fact]
    public void Flatten_Concat_SetsOperatorSymbolBraces()
    {
        ElaboratedDesign design = Flatten("""
            <contassign dtype_id="16">
              <concat dtype_id="16">
                <varref name="hi"/>
                <varref name="lo"/>
              </concat>
              <varref name="result"/>
            </contassign>
            """);

        DesignContAssign ca = Assert.Single(design.ModuleDefinitions["top"].ContAssigns);
        Assert.Equal("{}", ca.OperatorSymbol);
        Assert.Contains("hi", ca.SourceNames);
        Assert.Contains("lo", ca.SourceNames);
    }

    [Fact]
    public void Flatten_Cond_SetsOperatorSymbolTernary()
    {
        ElaboratedDesign design = Flatten("""
            <contassign dtype_id="8">
              <cond dtype_id="8">
                <varref name="sel"/>
                <varref name="a"/>
                <varref name="b"/>
              </cond>
              <varref name="out"/>
            </contassign>
            """);

        DesignContAssign ca = Assert.Single(design.ModuleDefinitions["top"].ContAssigns);
        Assert.Equal("?:", ca.OperatorSymbol);
        Assert.Contains("sel", ca.SourceNames);
        Assert.Contains("a",   ca.SourceNames);
        Assert.Contains("b",   ca.SourceNames);
    }

    [Theory]
    [InlineData("add", "+")]
    [InlineData("sub", "-")]
    [InlineData("and", "&")]
    [InlineData("or",  "|")]
    [InlineData("xor", "^")]
    [InlineData("eq",  "=")]
    [InlineData("lt",  "<")]
    [InlineData("shiftl", "<<")]
    public void Flatten_BinaryOp_SetsCorrectSymbol(string xmlTag, string expectedSymbol)
    {
        ElaboratedDesign design = Flatten($"""
            <contassign dtype_id="8">
              <{xmlTag} dtype_id="8">
                <varref name="a"/>
                <varref name="b"/>
              </{xmlTag}>
              <varref name="out"/>
            </contassign>
            """);

        DesignContAssign ca = Assert.Single(design.ModuleDefinitions["top"].ContAssigns);
        Assert.Equal(expectedSymbol, ca.OperatorSymbol);
    }

    // ── Sequential blocks not emitted as contassigns ──────────────────────────

    [Fact]
    public void Flatten_SequentialBlock_ProducesNoContAssign()
    {
        ElaboratedDesign design = Flatten("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <varref name="d"/>
                <varref name="q"/>
              </assigndly>
            </always>
            """);

        Assert.Empty(design.ModuleDefinitions["top"].ContAssigns);
    }

    // ── Instance ──────────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_Instance_PreservesPortConnections()
    {
        ElaboratedDesign design = Flatten("""
            <instance name="child_i" defName="child_mod" origName="child_i">
              <port name="clk" direction="in" portIndex="1"><varref name="clk"/></port>
            </instance>
            """);

        DesignInstanceDefinition inst = Assert.Single(design.ModuleDefinitions["top"].Instances);
        Assert.Equal("child_i",   inst.Name);
        Assert.Equal("child_mod", inst.ModuleName);
        DesignInstancePortConnection conn = Assert.Single(inst.PortConnections);
        Assert.Equal("clk", conn.SignalName);
    }
}
