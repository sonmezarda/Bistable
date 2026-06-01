namespace Bistable.Core.Design.Ast;

public sealed record PortDecl(
    string Name,
    SignalDirection Direction,
    int Width,
    bool IsSigned,
    int PinIndex);
