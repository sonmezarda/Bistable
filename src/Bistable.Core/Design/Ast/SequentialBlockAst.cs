namespace Bistable.Core.Design.Ast;

public sealed record SequentialBlockAst(
    IReadOnlyList<EdgeTrigger> Triggers,
    StatementAst Body,
    bool HasAsynchronousReset);
