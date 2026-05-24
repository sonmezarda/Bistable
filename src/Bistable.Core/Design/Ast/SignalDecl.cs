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
    StructTypeDecl? StructType = null);
