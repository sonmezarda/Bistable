using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

public sealed class StatementAstTests
{
    // ── AssignAst (non-blocking / blocking) ─────────────────────────────────

    [Fact]
    public void AssignAst_Assigndly_IsNonBlocking()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <assigndly dtype_id="8">
                  <varref name="data_in"/>
                  <varref name="reg_q"/>
                </assigndly>
              </begin>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        BeginAst begin = Assert.IsType<BeginAst>(block.Body);
        AssignAst assign = Assert.IsType<AssignAst>(Assert.Single(begin.Statements));
        Assert.True(assign.IsNonBlocking);
        Assert.Equal("reg_q",   Assert.IsType<VarRefLValue>(assign.Target).Name);
        Assert.Equal("data_in", Assert.IsType<SignalRef>(assign.Source).Name);
    }

    [Fact]
    public void AssignAst_Assign_IsBlocking()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <begin>
                <assign dtype_id="8">
                  <varref name="src"/>
                  <varref name="dst"/>
                </assign>
              </begin>
            </always>
            """);

        CombinationalBlockAst block = Assert.Single(ast.TopModule!.CombinationalBlocks);
        BeginAst begin = Assert.IsType<BeginAst>(block.Body);
        AssignAst assign = Assert.IsType<AssignAst>(Assert.Single(begin.Statements));
        Assert.False(assign.IsNonBlocking);
    }

    // ── IfAst ────────────────────────────────────────────────────────────────

    [Fact]
    public void IfAst_WithElse_ParsesBothBranches()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <if dtype_id="1">
                  <varref name="we"/>
                  <begin>
                    <assigndly dtype_id="8">
                      <varref name="data_in"/>
                      <varref name="reg_q"/>
                    </assigndly>
                  </begin>
                  <begin>
                    <assigndly dtype_id="8">
                      <varref name="zero"/>
                      <varref name="reg_q"/>
                    </assigndly>
                  </begin>
                </if>
              </begin>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        BeginAst outerBegin = Assert.IsType<BeginAst>(block.Body);
        IfAst ifStmt = Assert.IsType<IfAst>(Assert.Single(outerBegin.Statements));
        Assert.Equal("we", Assert.IsType<SignalRef>(ifStmt.Condition).Name);
        Assert.NotNull(ifStmt.Else);
    }

    [Fact]
    public void IfAst_WithoutElse_ElseIsNull()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <if dtype_id="1">
                <varref name="en"/>
                <assigndly dtype_id="8">
                  <varref name="src"/>
                  <varref name="dst"/>
                </assigndly>
              </if>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        IfAst ifStmt = Assert.IsType<IfAst>(block.Body);
        Assert.Null(ifStmt.Else);
    }

    // ── BeginAst ─────────────────────────────────────────────────────────────

    [Fact]
    public void BeginAst_MultipleStatements_ParsesAll()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <assigndly dtype_id="8"><varref name="a"/><varref name="q1"/></assigndly>
                <assigndly dtype_id="8"><varref name="b"/><varref name="q2"/></assigndly>
                <assigndly dtype_id="8"><varref name="c"/><varref name="q3"/></assigndly>
              </begin>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        BeginAst begin = Assert.IsType<BeginAst>(block.Body);
        Assert.Equal(3, begin.Statements.Count);
    }

    // ── Unknown statement ─────────────────────────────────────────────────────

    [Fact]
    public void UnknownStatement_SkippedWithEmptyBegin_DoesNotThrow()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin>
                <display_call/>
                <assigndly dtype_id="8"><varref name="src"/><varref name="dst"/></assigndly>
              </begin>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        BeginAst begin = Assert.IsType<BeginAst>(block.Body);
        // display_call becomes an empty BeginAst, assigndly becomes AssignAst
        Assert.Equal(2, begin.Statements.Count);
        Assert.IsType<AssignAst>(begin.Statements[1]);
    }
}
