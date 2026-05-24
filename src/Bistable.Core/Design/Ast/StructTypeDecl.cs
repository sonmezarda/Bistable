namespace Bistable.Core.Design.Ast;

/// <summary>
/// A packed-struct type (e.g. SystemVerilog <c>typedef struct packed { ... } ctrl_t;</c>).
/// Attached to <see cref="SignalDecl.StructType"/> when the signal is declared with a
/// struct type. Lets the schematic decoder render packed-struct field fan-out (P2-11)
/// with per-field labelled legs instead of collapsing every consumer onto the same
/// boundary pin.
/// </summary>
public sealed record StructTypeDecl(
    string Name,
    int TotalWidth,
    IReadOnlyList<StructFieldDecl> Fields);

/// <summary>
/// One field inside a packed struct. <see cref="Lo"/> + <see cref="Width"/> describe the
/// field's bit position in the struct (LSB-relative). Names are recovered from
/// Verilator's <c>&lt;memberdtype&gt;</c> entries inside the <c>&lt;typetable&gt;</c>.
/// </summary>
public sealed record StructFieldDecl(
    string FieldName,
    int Lo,
    int Width)
{
    public int Hi => Lo + Width - 1;
    public BitRange Range => new(Hi, Lo);
}
