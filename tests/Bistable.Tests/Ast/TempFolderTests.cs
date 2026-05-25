using Bistable.Core.Design.Ast;
using Bistable.Verilator;

namespace Bistable.Tests.Ast;

public sealed class TempFolderTests
{
    private static readonly SignalDecl A = new("a", 8, false, []);
    private static readonly SignalDecl B = new("b", 8, false, []);
    private static readonly SignalDecl C = new("c", 8, false, []);
    private static readonly SignalDecl Sel = new("sel", 1, false, []);

    [Fact]
    public void SingleConsumerTmp_RemovedFromAst()
    {
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_a", 8), B],
            contAssigns: [
                Assign("__VdfgTmp_a", new SignalRef("a")),
                Assign("b", new SignalRef("__VdfgTmp_a"))
            ]);

        Assert.DoesNotContain(folded.LocalSignals, s => s.Name == "__VdfgTmp_a");
        Assert.DoesNotContain(folded.ContAssigns, ca => LValueName(ca.Target) == "__VdfgTmp_a");
    }

    [Fact]
    public void SingleConsumerTmp_ConsumerExpressionContainsFolded()
    {
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_a", 8), B],
            contAssigns: [
                Assign("__VdfgTmp_a", new BinaryExpr(BinaryOp.Xor, new SignalRef("a"), new SignalRef("c"))),
                Assign("b", new SignalRef("__VdfgTmp_a"))
            ]);

        ContAssignAst consumer = Assert.Single(folded.ContAssigns);
        BinaryExpr expr = Assert.IsType<BinaryExpr>(consumer.Source);
        Assert.Equal(BinaryOp.Xor, expr.Op);
    }

    [Fact]
    public void MultiConsumerTmp_PreservedAsCseWin()
    {
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_a", 8), B, C],
            contAssigns: [
                Assign("__VdfgTmp_a", new BinaryExpr(BinaryOp.Xor, new SignalRef("a"), new SignalRef("b"))),
                Assign("b", new SignalRef("__VdfgTmp_a")),
                Assign("c", new SignalRef("__VdfgTmp_a"))
            ]);

        Assert.Contains(folded.LocalSignals, s => s.Name == "__VdfgTmp_a");
        Assert.Contains(folded.ContAssigns, ca => LValueName(ca.Target) == "__VdfgTmp_a");
    }

    [Fact]
    public void NestedTmps_FoldRecursively()
    {
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_a", 8), Tmp("__VdfgTmp_b", 8), B],
            contAssigns: [
                Assign("__VdfgTmp_a", new SignalRef("a")),
                Assign("__VdfgTmp_b", new SignalRef("__VdfgTmp_a")),
                Assign("b", new SignalRef("__VdfgTmp_b"))
            ]);

        ContAssignAst consumer = Assert.Single(folded.ContAssigns);
        SignalRef source = Assert.IsType<SignalRef>(consumer.Source);
        Assert.Equal("a", source.Name);
    }

    [Fact]
    public void NestedTmpLoop_BoundedTermination()
    {
        ModuleAst folded = FoldModule(
            locals: [Tmp("__VdfgTmp_a", 8), Tmp("__VdfgTmp_b", 8), C],
            contAssigns: [
                Assign("__VdfgTmp_a", new SignalRef("__VdfgTmp_b")),
                Assign("__VdfgTmp_b", new SignalRef("__VdfgTmp_a")),
                Assign("c", new SignalRef("__VdfgTmp_b"))
            ]);

        Assert.NotEmpty(folded.ContAssigns);
        Assert.Contains(folded.LocalSignals, s => s.Name.StartsWith("__V", StringComparison.Ordinal));
    }

    [Fact]
    public void WidthMismatch_LeavesTmpUnfolded()
    {
        ModuleAst folded = FoldModule(
            locals: [new SignalDecl("one_bit", 1, false, []), Tmp("__VdfgTmp_wide", 8), B],
            contAssigns: [
                Assign("__VdfgTmp_wide", new SignalRef("one_bit")),
                Assign("b", new SignalRef("__VdfgTmp_wide"))
            ]);

        Assert.Contains(folded.LocalSignals, s => s.Name == "__VdfgTmp_wide");
        Assert.Contains(folded.ContAssigns, ca => LValueName(ca.Target) == "__VdfgTmp_wide");
    }

    [Fact]
    public void BitSelectOnTmp_SubstitutesIntoBitSelect()
    {
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_a", 8), B],
            contAssigns: [
                Assign("__VdfgTmp_a", new SignalRef("a")),
                Assign("b", new BitSelectExpr(new SignalRef("__VdfgTmp_a"), new BitRange(3, 0)))
            ]);

        BitSelectExpr bitSelect = Assert.IsType<BitSelectExpr>(Assert.Single(folded.ContAssigns).Source);
        Assert.Equal("a", Assert.IsType<SignalRef>(bitSelect.Base).Name);
    }

    [Fact]
    public void NonInternalSignal_UnaffectedByFolder()
    {
        ModuleAst folded = FoldModule(
            locals: [A, new SignalDecl("tmp_user", 8, false, []), B],
            contAssigns: [
                Assign("tmp_user", new SignalRef("a")),
                Assign("b", new SignalRef("tmp_user"))
            ]);

        Assert.Contains(folded.LocalSignals, s => s.Name == "tmp_user");
        Assert.Equal(2, folded.ContAssigns.Count);
    }

    [Fact]
    public void EmptyModule_NoExceptions()
    {
        ModuleAst module = new("top", true, [], [], [], [], [], [], []);
        DesignAst folded = TempFolder.Fold(new DesignAst([module]));
        Assert.Empty(folded.TopModule!.ContAssigns);
    }

    [Fact]
    public void TmpInFFsD_Substituted()
    {
        AssignAst assign = new(new VarRefLValue("q"), new SignalRef("__VdfgTmp_d"), IsNonBlocking: true);
        ModuleAst folded = FoldModule(
            locals: [A, Tmp("__VdfgTmp_d", 8), new SignalDecl("q", 8, false, [], IsRegistered: true)],
            contAssigns: [Assign("__VdfgTmp_d", new SignalRef("a"))],
            sequentialBlocks: [new SequentialBlockAst([new EdgeTrigger(EdgeKind.Rising, "clk")], assign, false)]);

        AssignAst foldedAssign = Assert.IsType<AssignAst>(Assert.Single(folded.SequentialBlocks).Body);
        Assert.Equal("a", Assert.IsType<SignalRef>(foldedAssign.Source).Name);
        Assert.Empty(folded.ContAssigns);
    }

    [Fact]
    public void TmpInMuxCondition_Substituted()
    {
        ModuleAst folded = FoldModule(
            locals: [Sel, Tmp("__VdfgTmp_sel", 1), A, B, C],
            contAssigns: [
                Assign("__VdfgTmp_sel", new SignalRef("sel")),
                Assign("c", new CondExpr(new SignalRef("__VdfgTmp_sel"), new SignalRef("a"), new SignalRef("b")))
            ]);

        CondExpr mux = Assert.IsType<CondExpr>(Assert.Single(folded.ContAssigns).Source);
        Assert.Equal("sel", Assert.IsType<SignalRef>(mux.Condition).Name);
    }

    [Fact]
    public void EndToEnd_ArnicompTopVdfgTmpsAreFoldedWhenSingleConsumer()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "samples", "arnicomp", ".bistable", "metadata", "arnicomp_top.xml");

        if (!File.Exists(path))
            return;

        DesignAst ast = new VerilatorXmlAstReader().Read(Path.GetFullPath(path));
        foreach (ModuleAst module in ast.Modules)
        {
            HashSet<string> tmpDefs = module.ContAssigns
                .Select(ca => LValueName(ca.Target))
                .Where(IsInternal)
                .ToHashSet(StringComparer.Ordinal);

            Dictionary<string, int> counts = tmpDefs.ToDictionary(static n => n, static _ => 0, StringComparer.Ordinal);
            foreach (ContAssignAst ca in module.ContAssigns)
                CountRefs(ca.Source, counts);
            foreach (SequentialBlockAst block in module.SequentialBlocks)
                CountRefs(block.Body, counts);

            Assert.DoesNotContain(counts, kv => kv.Value == 1);
        }
    }

    private static ModuleAst FoldModule(
        IReadOnlyList<SignalDecl> locals,
        IReadOnlyList<ContAssignAst> contAssigns,
        IReadOnlyList<SequentialBlockAst>? sequentialBlocks = null)
    {
        ModuleAst module = new("top", true, [], [], locals, [], contAssigns, sequentialBlocks ?? [], []);
        return TempFolder.Fold(new DesignAst([module])).TopModule!;
    }

    private static SignalDecl Tmp(string name, int width) => new(name, width, false, []);

    private static ContAssignAst Assign(string target, ExpressionAst source) => new(new VarRefLValue(target), source);

    private static string LValueName(LValueAst lval) => lval switch
    {
        VarRefLValue v => v.Name,
        BitSelectLValue b => b.SignalName,
        ArraySelectLValue a => a.SignalName,
        StructFieldLValue sf => sf.SignalName,
        ConcatLValue c => c.Parts.Count > 0 ? LValueName(c.Parts[0]) : string.Empty,
        _ => string.Empty
    };

    private static bool IsInternal(string name) =>
        name.StartsWith("__V", StringComparison.Ordinal);

    private static void CountRefs(StatementAst stmt, Dictionary<string, int> counts)
    {
        switch (stmt)
        {
            case AssignAst assign:
                CountRefs(assign.Source, counts);
                break;
            case BeginAst begin:
                foreach (StatementAst child in begin.Statements) CountRefs(child, counts);
                break;
            case IfAst ifAst:
                CountRefs(ifAst.Condition, counts);
                CountRefs(ifAst.Then, counts);
                if (ifAst.Else is not null) CountRefs(ifAst.Else, counts);
                break;
            case CaseAst caseAst:
                CountRefs(caseAst.Subject, counts);
                foreach (CaseArm arm in caseAst.Arms)
                {
                    CountRefs(arm.Label, counts);
                    CountRefs(arm.Body, counts);
                }
                if (caseAst.Default is not null) CountRefs(caseAst.Default, counts);
                break;
        }
    }

    private static void CountRefs(ExpressionAst expr, Dictionary<string, int> counts)
    {
        switch (expr)
        {
            case SignalRef s when counts.ContainsKey(s.Name):
                counts[s.Name]++;
                break;
            case BitSelectExpr bs:
                CountRefs(bs.Base, counts);
                break;
            case ArraySelectExpr arr:
                CountRefs(arr.Base, counts);
                CountRefs(arr.Index, counts);
                break;
            case ConcatExpr concat:
                foreach (ExpressionAst part in concat.Parts) CountRefs(part, counts);
                break;
            case ReplicateExpr rep:
                CountRefs(rep.Pattern, counts);
                break;
            case ExtendExpr ext:
                CountRefs(ext.Inner, counts);
                break;
            case BinaryExpr bin:
                CountRefs(bin.Left, counts);
                CountRefs(bin.Right, counts);
                break;
            case UnaryExpr un:
                CountRefs(un.Operand, counts);
                break;
            case CondExpr cond:
                CountRefs(cond.Condition, counts);
                CountRefs(cond.IfTrue, counts);
                CountRefs(cond.IfFalse, counts);
                break;
            case FunctionCallExpr fn:
                foreach (ExpressionAst arg in fn.Args) CountRefs(arg, counts);
                break;
        }
    }
}
