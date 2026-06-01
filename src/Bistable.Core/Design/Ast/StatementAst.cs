using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Ast;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(BeginAst),  "BeginAst")]
[JsonDerivedType(typeof(IfAst),     "IfAst")]
[JsonDerivedType(typeof(CaseAst),   "CaseAst")]
[JsonDerivedType(typeof(AssignAst), "AssignAst")]

public abstract record StatementAst;

public sealed record BeginAst(IReadOnlyList<StatementAst> Statements) : StatementAst;

public sealed record IfAst(
    ExpressionAst Condition,
    StatementAst Then,
    StatementAst? Else) : StatementAst;

public sealed record CaseAst(
    ExpressionAst Subject,
    IReadOnlyList<CaseArm> Arms,
    StatementAst? Default) : StatementAst;

public sealed record AssignAst(
    LValueAst Target,
    ExpressionAst Source,
    bool IsNonBlocking) : StatementAst;

public sealed record CaseArm(ExpressionAst Label, StatementAst Body);
