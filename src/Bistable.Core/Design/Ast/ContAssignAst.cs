namespace Bistable.Core.Design.Ast;

public sealed record ContAssignAst(LValueAst Target, ExpressionAst Source);
