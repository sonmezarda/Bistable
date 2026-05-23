namespace Bistable.Core.Design.Ast;

public sealed record DesignAst(IReadOnlyList<ModuleAst> Modules)
{
    public ModuleAst? TopModule => Modules.FirstOrDefault(static m => m.IsTop);
}
