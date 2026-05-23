namespace Bistable.Core.Design.Ast;

public sealed record ModuleAst(
    string Name,
    bool IsTop,
    IReadOnlyList<PortDecl> Ports,
    IReadOnlyList<DesignParameter> Parameters,
    IReadOnlyList<SignalDecl> LocalSignals,
    IReadOnlyList<InstanceDecl> Instances,
    IReadOnlyList<ContAssignAst> ContAssigns,
    IReadOnlyList<SequentialBlockAst> SequentialBlocks,
    IReadOnlyList<CombinationalBlockAst> CombinationalBlocks);
