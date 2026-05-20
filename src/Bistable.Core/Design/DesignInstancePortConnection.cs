namespace Bistable.Core.Design;

public sealed record DesignInstancePortConnection(
    string PortName,
    string SignalName,
    string Direction,
    int PortIndex);
