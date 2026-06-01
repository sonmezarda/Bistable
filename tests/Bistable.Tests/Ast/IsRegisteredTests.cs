using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

public sealed class IsRegisteredTests
{
    [Fact]
    public void SignalDrivenInSequentialBlock_IsRegisteredTrue()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="q" dtype_id="8" vartype="logic"/>
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

        SignalDecl q = Assert.Single(ast.TopModule!.LocalSignals, s => s.Name == "q");
        Assert.True(q.IsRegistered);
    }

    [Fact]
    public void SignalDrivenOnlyFromContAssign_IsRegisteredFalse()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="w" dtype_id="8" vartype="logic"/>
            <contassign dtype_id="8">
              <varref name="src"/>
              <varref name="w"/>
            </contassign>
            """);

        SignalDecl w = Assert.Single(ast.TopModule!.LocalSignals, s => s.Name == "w");
        Assert.False(w.IsRegistered);
    }

    [Fact]
    public void SignalDrivenInsideIfInSequentialBlock_IsRegisteredTrue()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="reg_q" dtype_id="8" vartype="logic"/>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <if dtype_id="1">
                  <varref name="we"/>
                  <assigndly dtype_id="8">
                    <varref name="data_in"/>
                    <varref name="reg_q"/>
                  </assigndly>
                </if>
              </begin>
            </always>
            """);

        SignalDecl regQ = Assert.Single(ast.TopModule!.LocalSignals, s => s.Name == "reg_q");
        Assert.True(regQ.IsRegistered);
    }

    [Fact]
    public void PortSignals_NeverMarkedRegistered()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline(
            """
            <var name="clk" dtype_id="1" dir="input" pinIndex="1" vartype="logic"/>
            <var name="out" dtype_id="8" dir="output" pinIndex="2" vartype="logic"/>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <varref name="out"/>
                <varref name="out"/>
              </assigndly>
            </always>
            """);

        // Ports are PortDecl, not SignalDecl — LocalSignals should be empty
        Assert.Empty(ast.TopModule!.LocalSignals);
    }

    [Fact]
    public void MultipleSignals_OnlySequentialTargetIsRegistered()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <var name="comb_w" dtype_id="8" vartype="logic"/>
            <var name="ff_q"   dtype_id="8" vartype="logic"/>
            <contassign dtype_id="8">
              <varref name="src"/>
              <varref name="comb_w"/>
            </contassign>
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <assigndly dtype_id="8">
                <varref name="comb_w"/>
                <varref name="ff_q"/>
              </assigndly>
            </always>
            """);

        SignalDecl combW = Assert.Single(ast.TopModule!.LocalSignals, s => s.Name == "comb_w");
        SignalDecl ffQ   = Assert.Single(ast.TopModule!.LocalSignals, s => s.Name == "ff_q");
        Assert.False(combW.IsRegistered);
        Assert.True(ffQ.IsRegistered);
    }
}
