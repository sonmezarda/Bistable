using System.Text.Json.Serialization;

namespace Bistable.Core.Design.Ast;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(VarRefLValue),      "VarRefLValue")]
[JsonDerivedType(typeof(BitSelectLValue),   "BitSelectLValue")]
[JsonDerivedType(typeof(ArraySelectLValue), "ArraySelectLValue")]
[JsonDerivedType(typeof(ConcatLValue),      "ConcatLValue")]
[JsonDerivedType(typeof(StructFieldLValue), "StructFieldLValue")]
public abstract record LValueAst;

public sealed record VarRefLValue(string Name) : LValueAst;

public sealed record BitSelectLValue(string SignalName, BitRange Range) : LValueAst;

public sealed record ArraySelectLValue(string SignalName, ExpressionAst Index) : LValueAst;

public sealed record ConcatLValue(IReadOnlyList<LValueAst> Parts) : LValueAst;

public sealed record StructFieldLValue(string SignalName, string FieldName) : LValueAst;
