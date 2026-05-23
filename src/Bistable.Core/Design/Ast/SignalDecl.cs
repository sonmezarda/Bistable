namespace Bistable.Core.Design.Ast;

public sealed record SignalDecl(
    string Name,
    int Width,
    bool IsSigned,
    IReadOnlyList<BitRange> ArrayDims,
    bool IsRegistered = false);
