using Bistable.Core.Design;
using Bistable.Core.Design.Ast;
using Bistable.Core.Design.Ast.Passes;

namespace Bistable.Tests.Ast;

public sealed class AstModuleDiffTests
{
    [Fact]
    public void Compare_InternalLogicChange_MarksOnlyModuleDirtyAndKeepsInterface()
    {
        DesignAst before = new([Module("top", new ConstExpr(0, 1, false)), Module("untouched", new ConstExpr(0, 1, false))]);
        DesignAst after = new([Module("top", new ConstExpr(1, 1, false)), Module("untouched", new ConstExpr(0, 1, false))]);

        AstModuleDiffResult diff = AstModuleDiff.Compare(before, after, "top");

        Assert.Equal(["top"], diff.DirtyModules);
        Assert.False(diff.TopInterfaceChanged);
        Assert.Equal(diff.PreviousHashes["untouched"], diff.CurrentHashes["untouched"]);
    }

    [Fact]
    public void Compare_PortWidthChange_MarksTopInterfaceChanged()
    {
        DesignAst before = new([Module("top", new ConstExpr(0, 1, false), width: 1)]);
        DesignAst after = new([Module("top", new ConstExpr(0, 2, false), width: 2)]);

        AstModuleDiffResult diff = AstModuleDiff.Compare(before, after, "top");

        Assert.True(diff.TopInterfaceChanged);
        Assert.Contains("top", diff.DirtyModules);
    }

    [Fact]
    public void Compare_AddedAndRemovedModules_AreDirty()
    {
        DesignAst before = new([Module("top", new ConstExpr(0, 1, false)), Module("old", new ConstExpr(0, 1, false))]);
        DesignAst after = new([Module("top", new ConstExpr(0, 1, false)), Module("new", new ConstExpr(0, 1, false))]);

        AstModuleDiffResult diff = AstModuleDiff.Compare(before, after, "top");

        Assert.Contains("old", diff.RemovedModules);
        Assert.Contains("new", diff.AddedModules);
    }

    private static ModuleAst Module(string name, ExpressionAst value, int width = 1) => new(
        name,
        IsTop: name == "top",
        Ports: [new PortDecl("y", SignalDirection.Output, width, false, 0)],
        Parameters: [],
        LocalSignals: [],
        Instances: [],
        ContAssigns: [new ContAssignAst(new VarRefLValue("y"), value)],
        SequentialBlocks: [],
        CombinationalBlocks: []);
}
