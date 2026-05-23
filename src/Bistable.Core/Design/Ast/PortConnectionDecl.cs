namespace Bistable.Core.Design.Ast;

public sealed record PortConnectionDecl(
    string PortName,
    string SignalName,
    string Direction,
    int PortIndex);
