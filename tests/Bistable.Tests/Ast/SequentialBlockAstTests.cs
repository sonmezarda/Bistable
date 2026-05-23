using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

public sealed class SequentialBlockAstTests
{
    // ── Edge triggers ────────────────────────────────────────────────────────

    [Fact]
    public void SequentialBlock_PosEdgeClk_SingleTriggerRising()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin/>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        EdgeTrigger trigger = Assert.Single(block.Triggers);
        Assert.Equal(EdgeKind.Rising, trigger.Edge);
        Assert.Equal("clk", trigger.SignalName);
    }

    [Fact]
    public void SequentialBlock_AsyncReset_TwoTriggersAndFlagSet()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
                <senitem edgeType="NEG"><varref name="rst_n"/></senitem>
              </sentree>
              <begin/>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        Assert.Equal(2, block.Triggers.Count);
        Assert.Equal(EdgeKind.Rising,  block.Triggers[0].Edge);
        Assert.Equal(EdgeKind.Falling, block.Triggers[1].Edge);
        Assert.True(block.HasAsynchronousReset);
    }

    [Fact]
    public void SequentialBlock_SyncResetOnly_HasAsynchronousResetFalse()
    {
        // A posedge-only always block has no async reset
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin/>
            </always>
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        Assert.False(block.HasAsynchronousReset);
    }

    // ── Identification: with vs without sentree ───────────────────────────────

    [Fact]
    public void AlwaysWithSentree_IsSequentialBlock()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <sentree>
                <senitem edgeType="POS"><varref name="clk"/></senitem>
              </sentree>
              <begin/>
            </always>
            """);

        Assert.Single(ast.TopModule!.SequentialBlocks);
        Assert.Empty(ast.TopModule!.CombinationalBlocks);
    }

    [Fact]
    public void AlwaysWithoutSentree_IsCombinationalBlock()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <always>
              <begin>
                <assign dtype_id="8"><varref name="src"/><varref name="dst"/></assign>
              </begin>
            </always>
            """);

        Assert.Empty(ast.TopModule!.SequentialBlocks);
        Assert.Single(ast.TopModule!.CombinationalBlocks);
    }

    // ── Full arnicomp example (master plan §16) ───────────────────────────────

    [Fact]
    public void SequentialBlock_ArnicompExample_ParsesFullTree()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
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
            """);

        SequentialBlockAst block = Assert.Single(ast.TopModule!.SequentialBlocks);
        Assert.True(block.HasAsynchronousReset);

        BeginAst begin = Assert.IsType<BeginAst>(block.Body);
        AssignAst assign = Assert.IsType<AssignAst>(Assert.Single(begin.Statements));
        Assert.True(assign.IsNonBlocking);
        Assert.Equal("inst_q", Assert.IsType<VarRefLValue>(assign.Target).Name);

        CondExpr cond = Assert.IsType<CondExpr>(assign.Source);
        Assert.Equal("rst_n",       Assert.IsType<SignalRef>(cond.Condition).Name);
        Assert.Equal("instruction", Assert.IsType<SignalRef>(cond.IfTrue).Name);
        ConstExpr zero = Assert.IsType<ConstExpr>(cond.IfFalse);
        Assert.Equal(0, (int)zero.Value);
        Assert.Equal(8, zero.Width);
    }
}
