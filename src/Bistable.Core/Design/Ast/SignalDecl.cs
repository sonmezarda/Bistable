namespace Bistable.Core.Design.Ast;

public sealed record SignalDecl(
    string Name,
    int Width,
    bool IsSigned,
    IReadOnlyList<BitRange> ArrayDims,
    bool IsRegistered = false,
    // P2-11: when the signal is declared with a packed struct type, the resolved
    // type metadata lives here. The schematic decoder uses it to emit per-field
    // fan-out instead of a single collapsed wire to all consumers.
    StructTypeDecl? StructType = null,
    // Verilator's `origName` attribute for the var node. Differs from Name
    // when the source identifier was escaped (e.g. a Yosys-flattened wire
    // `\u_alu.carry` shows up with Name="u_alu.carry" but origName="u_alu__02ecarry"
    // — and that is the actual C++ field on the model class). Probe table
    // generation needs the mangled form to address the real Verilator field.
    string? OrigName = null);
