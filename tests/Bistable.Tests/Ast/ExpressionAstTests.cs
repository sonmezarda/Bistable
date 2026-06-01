using Bistable.Core.Design.Ast;

namespace Bistable.Tests.Ast;

public sealed class ExpressionAstTests
{
    // ── SignalRef ────────────────────────────────────────────────────────────

    [Fact]
    public void SignalRef_Varref_ParsesName()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
              <varref name="src"/>
              <varref name="dst"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        SignalRef src = Assert.IsType<SignalRef>(ca.Source);
        Assert.Equal("src", src.Name);
    }

    // ── ConstExpr ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("8'h0",  0,  8)]
    [InlineData("8'hFF", 255, 8)]
    [InlineData("4'b1010", 10, 4)]
    [InlineData("32'd12",  12, 32)]
    public void ConstExpr_VariousBases_ParsesValueAndWidth(string literal, int expectedValue, int expectedWidth)
    {
        DesignAst ast = AstReaderTestHelper.ParseInline($"""
            <contassign dtype_id="1">
              <const name="{literal}"/>
              <varref name="dst"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        ConstExpr c = Assert.IsType<ConstExpr>(ca.Source);
        Assert.Equal(expectedValue, (int)c.Value);
        Assert.Equal(expectedWidth, c.Width);
    }

    // ── BitSelectExpr (<sel>) ────────────────────────────────────────────────

    [Fact]
    public void BitSelectExpr_Sel_ParsesBaseAndRange()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
              <sel dtype_id="2">
                <varref name="bus"/>
                <const name="32'h6"/>
                <const name="32'h2"/>
              </sel>
              <varref name="slice_out"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        BitSelectExpr sel = Assert.IsType<BitSelectExpr>(ca.Source);
        SignalRef baseRef = Assert.IsType<SignalRef>(sel.Base);
        Assert.Equal("bus", baseRef.Name);
        Assert.Equal(7, sel.Range.Hi);
        Assert.Equal(6, sel.Range.Lo);
    }

    // ── ConcatExpr (<concat>) ────────────────────────────────────────────────

    [Fact]
    public void ConcatExpr_TwoParts_ParsesMsbFirst()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
              <concat dtype_id="16">
                <varref name="hi"/>
                <varref name="lo"/>
              </concat>
              <varref name="result"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        ConcatExpr concat = Assert.IsType<ConcatExpr>(ca.Source);
        Assert.Equal(2, concat.Parts.Count);
        Assert.Equal("hi", Assert.IsType<SignalRef>(concat.Parts[0]).Name);
        Assert.Equal("lo", Assert.IsType<SignalRef>(concat.Parts[1]).Name);
    }

    // ── CondExpr (<cond>) ────────────────────────────────────────────────────

    [Fact]
    public void CondExpr_Cond_ParsesThreeParts()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
              <cond dtype_id="8">
                <varref name="sel"/>
                <varref name="a"/>
                <varref name="b"/>
              </cond>
              <varref name="out"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        CondExpr cond = Assert.IsType<CondExpr>(ca.Source);
        Assert.Equal("sel", Assert.IsType<SignalRef>(cond.Condition).Name);
        Assert.Equal("a",   Assert.IsType<SignalRef>(cond.IfTrue).Name);
        Assert.Equal("b",   Assert.IsType<SignalRef>(cond.IfFalse).Name);
    }

    [Fact]
    public void CondExpr_Nested_ParsesRecursively()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
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
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        CondExpr outer = Assert.IsType<CondExpr>(ca.Source);
        CondExpr inner = Assert.IsType<CondExpr>(outer.IfFalse);
        Assert.Equal("sel0", Assert.IsType<SignalRef>(inner.Condition).Name);
        Assert.Equal("c",    Assert.IsType<SignalRef>(inner.IfFalse).Name);
    }

    // ── BinaryExpr ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("add", BinaryOp.Add)]
    [InlineData("sub", BinaryOp.Sub)]
    [InlineData("and", BinaryOp.And)]
    [InlineData("or",  BinaryOp.Or)]
    [InlineData("xor", BinaryOp.Xor)]
    [InlineData("eq",  BinaryOp.Equal)]
    [InlineData("lt",  BinaryOp.LessThan)]
    [InlineData("shiftl", BinaryOp.ShiftLeft)]
    public void BinaryExpr_KnownOp_ParsesCorrectly(string xmlTag, BinaryOp expectedOp)
    {
        DesignAst ast = AstReaderTestHelper.ParseInline($"""
            <contassign dtype_id="1">
              <{xmlTag} dtype_id="8">
                <varref name="a"/>
                <varref name="b"/>
              </{xmlTag}>
              <varref name="out"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        BinaryExpr bin = Assert.IsType<BinaryExpr>(ca.Source);
        Assert.Equal(expectedOp, bin.Op);
        Assert.Equal("a", Assert.IsType<SignalRef>(bin.Left).Name);
        Assert.Equal("b", Assert.IsType<SignalRef>(bin.Right).Name);
    }

    // ── UnaryExpr ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not",    UnaryOp.Not)]
    [InlineData("lognot", UnaryOp.LogicNot)]
    [InlineData("negate", UnaryOp.Negate)]
    public void UnaryExpr_KnownOp_ParsesCorrectly(string xmlTag, UnaryOp expectedOp)
    {
        DesignAst ast = AstReaderTestHelper.ParseInline($"""
            <contassign dtype_id="1">
              <{xmlTag} dtype_id="8">
                <varref name="x"/>
              </{xmlTag}>
              <varref name="out"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        UnaryExpr un = Assert.IsType<UnaryExpr>(ca.Source);
        Assert.Equal(expectedOp, un.Op);
        Assert.Equal("x", Assert.IsType<SignalRef>(un.Operand).Name);
    }

    // ── Unknown expression ───────────────────────────────────────────────────

    [Fact]
    public void UnknownExpression_ReturnsZeroConst_DoesNotThrow()
    {
        DesignAst ast = AstReaderTestHelper.ParseInline("""
            <contassign dtype_id="1">
              <unknown_future_element/>
              <varref name="dst"/>
            </contassign>
            """);

        ContAssignAst ca = Assert.Single(ast.TopModule!.ContAssigns);
        ConstExpr fallback = Assert.IsType<ConstExpr>(ca.Source);
        Assert.Equal(0, (int)fallback.Value);
    }
}
