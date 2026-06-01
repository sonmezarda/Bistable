using System.Numerics;
using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Ast;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SignalRef),         "SignalRef")]
[JsonDerivedType(typeof(ConstExpr),         "ConstExpr")]
[JsonDerivedType(typeof(BitSelectExpr),     "BitSelectExpr")]
[JsonDerivedType(typeof(ArraySelectExpr),   "ArraySelectExpr")]
[JsonDerivedType(typeof(ConcatExpr),        "ConcatExpr")]
[JsonDerivedType(typeof(ReplicateExpr),     "ReplicateExpr")]
[JsonDerivedType(typeof(ExtendExpr),        "ExtendExpr")]
[JsonDerivedType(typeof(BinaryExpr),        "BinaryExpr")]
[JsonDerivedType(typeof(UnaryExpr),         "UnaryExpr")]
[JsonDerivedType(typeof(CondExpr),          "CondExpr")]
[JsonDerivedType(typeof(FunctionCallExpr),  "FunctionCallExpr")]
public abstract record ExpressionAst;

public sealed record SignalRef(string Name) : ExpressionAst;

/// <summary>
/// Numeric literal. <paramref name="IsHighImpedance"/> distinguishes the
/// special <c>'z</c> value used in tri-state assignments — Verilator encodes
/// it as a const node with the high-impedance flag; the decoder recognises it
/// to emit <c>TriStatePrimitive</c> instead of a Buffer.
/// </summary>
public sealed record ConstExpr(BigInteger Value, int Width, bool IsSigned, bool IsHighImpedance = false) : ExpressionAst;

public sealed record BitSelectExpr(ExpressionAst Base, BitRange Range) : ExpressionAst;

public sealed record ArraySelectExpr(ExpressionAst Base, ExpressionAst Index) : ExpressionAst;

public sealed record ConcatExpr(IReadOnlyList<ExpressionAst> Parts) : ExpressionAst;

public sealed record ReplicateExpr(int Count, ExpressionAst Pattern) : ExpressionAst;

public sealed record ExtendExpr(ExpressionAst Inner, int TargetWidth, bool IsSigned) : ExpressionAst;

public sealed record BinaryExpr(BinaryOp Op, ExpressionAst Left, ExpressionAst Right) : ExpressionAst;

public sealed record UnaryExpr(UnaryOp Op, ExpressionAst Operand) : ExpressionAst;

public sealed record CondExpr(
    ExpressionAst Condition,
    ExpressionAst IfTrue,
    ExpressionAst IfFalse) : ExpressionAst;

public sealed record FunctionCallExpr(
    string Name,
    IReadOnlyList<ExpressionAst> Args) : ExpressionAst;
