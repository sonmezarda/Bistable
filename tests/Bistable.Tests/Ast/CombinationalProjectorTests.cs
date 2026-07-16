using System.Numerics;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Ast.Passes;

namespace Bistable.Tests.Ast;

public sealed class CombinationalProjectorTests
{
    [Fact]
    public void Project_BeginBlock_LastAssignmentWins()
    {
        ModuleAst projected = Project(new BeginAst(
        [
            Assign("y", new SignalRef("a")),
            Assign("y", new SignalRef("b")),
        ]));

        ContAssignAst assignment = Assert.Single(projected.ContAssigns);
        Assert.Equal(new VarRefLValue("y"), assignment.Target);
        Assert.Equal(new SignalRef("b"), assignment.Source);
        Assert.Equal(CombinationalProjectionStatus.Projected, Result(projected).Status);
    }

    [Fact]
    public void Project_IfAfterDefault_UsesPreviousValueForUnassignedBranch()
    {
        ModuleAst projected = Project(new BeginAst(
        [
            Assign("y", new SignalRef("fallback")),
            new IfAst(
                new SignalRef("sel"),
                Assign("y", new SignalRef("selected")),
                Else: null),
        ]));

        CondExpr expression = Assert.IsType<CondExpr>(Assert.Single(projected.ContAssigns).Source);
        Assert.Equal(new SignalRef("sel"), expression.Condition);
        Assert.Equal(new SignalRef("selected"), expression.IfTrue);
        Assert.Equal(new SignalRef("fallback"), expression.IfFalse);
    }

    [Fact]
    public void Project_IfWithoutDefault_ReportsLatchRisk()
    {
        ModuleAst projected = Project(new IfAst(
            new SignalRef("sel"),
            Assign("y", new SignalRef("a")),
            Else: null));

        Assert.Empty(projected.ContAssigns);
        CombinationalProjectionTarget result = Result(projected);
        Assert.Equal(CombinationalProjectionStatus.Unsupported, result.Status);
        Assert.Contains("latch risk", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["a", "sel"], result.ReadSignals);
    }

    [Fact]
    public void Project_CaseWithConstantLabelsAndDefault_BuildsPriorityCondChain()
    {
        ModuleAst projected = Project(new CaseAst(
            new SignalRef("opcode"),
            [
                new CaseArm(Const(0, 2), Assign("y", new SignalRef("a"))),
                new CaseArm(Const(1, 2), Assign("y", new SignalRef("b"))),
            ],
            Assign("y", new SignalRef("fallback"))));

        CondExpr first = Assert.IsType<CondExpr>(Assert.Single(projected.ContAssigns).Source);
        AssertCaseCondition(first.Condition, "opcode", 0);
        Assert.Equal(new SignalRef("a"), first.IfTrue);

        CondExpr second = Assert.IsType<CondExpr>(first.IfFalse);
        AssertCaseCondition(second.Condition, "opcode", 1);
        Assert.Equal(new SignalRef("b"), second.IfTrue);
        Assert.Equal(new SignalRef("fallback"), second.IfFalse);
    }

    [Fact]
    public void Project_CaseWithNonConstantLabel_ReportsUnsupportedTarget()
    {
        ModuleAst projected = Project(new CaseAst(
            new SignalRef("opcode"),
            [new CaseArm(new SignalRef("dynamic_label"), Assign("y", new SignalRef("a")))],
            Assign("y", new SignalRef("fallback"))));

        Assert.Empty(projected.ContAssigns);
        CombinationalProjectionTarget result = Result(projected);
        Assert.Equal(CombinationalProjectionStatus.Unsupported, result.Status);
        Assert.Contains("not a constant", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dynamic_label", result.ReadSignals);
        Assert.Contains("opcode", result.ReadSignals);
    }

    [Fact]
    public void Project_ExpressionBeyondDepthLimit_ReportsUnsupportedTarget()
    {
        ExpressionAst expression = new SignalRef("a");
        for (int i = 0; i < CombinationalProjector.MaxExpressionDepth; i++)
        {
            expression = new UnaryExpr(UnaryOp.Not, expression);
        }

        ModuleAst projected = Project(Assign("y", expression));

        Assert.Empty(projected.ContAssigns);
        Assert.Contains("maximum depth", Result(projected).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_IsIdempotent()
    {
        ModuleAst once = Project(Assign("y", new SignalRef("a")));

        ModuleAst twice = CombinationalProjector.Project(once);

        Assert.Single(twice.ContAssigns);
        Assert.Single(twice.CombinationalBlocks[0].ProjectionResults!);
    }

    [Fact]
    public void Project_DisjointBitSliceWrites_ReconstructsOneWholeBusAssignment()
    {
        ModuleAst projected = ProjectWithLocals(
            new BeginAst(
            [
                BitAssign("bus", new BitRange(1, 0), new SignalRef("low")),
                BitAssign("bus", new BitRange(3, 2), new SignalRef("high")),
            ]),
            new SignalDecl("bus", 4, false, []));

        ContAssignAst assignment = Assert.Single(projected.ContAssigns);
        Assert.Equal(new VarRefLValue("bus"), assignment.Target);
        ConcatExpr concat = Assert.IsType<ConcatExpr>(assignment.Source);
        Assert.Equal([new SignalRef("high"), new SignalRef("low")], concat.Parts);
        Assert.Equal(CombinationalProjectionStatus.Projected, Result(projected).Status);
    }

    [Fact]
    public void Project_IncompleteBitSliceWrites_ReportLatchRisk()
    {
        ModuleAst projected = ProjectWithLocals(
            BitAssign("bus", new BitRange(1, 0), new SignalRef("low")),
            new SignalDecl("bus", 4, false, []));

        Assert.Empty(projected.ContAssigns);
        Assert.Contains("latch risk", Result(projected).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_BitSliceOverrideAfterWholeDefault_PreservesOneConditionalBusDriver()
    {
        ModuleAst projected = ProjectWithLocals(
            new BeginAst(
            [
                Assign("bus", new SignalRef("fallback")),
                new IfAst(
                    new SignalRef("sel"),
                    BitAssign("bus", new BitRange(0, 0), new SignalRef("bit_value")),
                    Else: null),
            ]),
            new SignalDecl("bus", 4, false, []));

        ContAssignAst assignment = Assert.Single(projected.ContAssigns);
        Assert.Equal(new VarRefLValue("bus"), assignment.Target);
        Assert.IsType<CondExpr>(assignment.Source);
    }

    [Fact]
    public void Project_WholeAssignmentSourceWidth_RecoversMissingDeclaredStructWidth()
    {
        ModuleAst projected = Project(new BeginAst(
        [
            Assign("ctrl", Const(0, 4)),
            BitAssign("ctrl", new BitRange(3, 3), new SignalRef("enable")),
        ]));

        ContAssignAst assignment = Assert.Single(projected.ContAssigns);
        Assert.Equal(new VarRefLValue("ctrl"), assignment.Target);
        Assert.Equal(CombinationalProjectionStatus.Projected, Result(projected).Status);
    }

    private static ModuleAst Project(StatementAst body) => CombinationalProjector.Project(new ModuleAst(
        Name: "top",
        IsTop: true,
        Ports: [],
        Parameters: [],
        LocalSignals: [],
        Instances: [],
        ContAssigns: [],
        SequentialBlocks: [],
        CombinationalBlocks: [new CombinationalBlockAst(body)]));

    private static AssignAst Assign(string target, ExpressionAst source) =>
        new(new VarRefLValue(target), source, IsNonBlocking: false);

    private static AssignAst BitAssign(string target, BitRange range, ExpressionAst source) =>
        new(new BitSelectLValue(target, range), source, IsNonBlocking: false);

    private static ModuleAst ProjectWithLocals(StatementAst body, params SignalDecl[] locals) =>
        CombinationalProjector.Project(new ModuleAst(
            Name: "top",
            IsTop: true,
            Ports: [],
            Parameters: [],
            LocalSignals: locals,
            Instances: [],
            ContAssigns: [],
            SequentialBlocks: [],
            CombinationalBlocks: [new CombinationalBlockAst(body)]));

    private static ConstExpr Const(int value, int width) =>
        new(new BigInteger(value), width, IsSigned: false);

    private static CombinationalProjectionTarget Result(ModuleAst module) =>
        Assert.Single(Assert.Single(module.CombinationalBlocks).ProjectionResults!);

    private static void AssertCaseCondition(ExpressionAst expression, string selector, int label)
    {
        BinaryExpr equality = Assert.IsType<BinaryExpr>(expression);
        Assert.Equal(BinaryOp.Equal, equality.Op);
        Assert.Equal(new SignalRef(selector), equality.Left);
        Assert.Equal(label, (int)Assert.IsType<ConstExpr>(equality.Right).Value);
    }
}
