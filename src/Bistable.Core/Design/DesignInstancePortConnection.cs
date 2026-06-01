namespace Bistable.Core.Design;

public sealed record DesignInstancePortConnection(
    string PortName,
    string SignalName,
    string Direction,
    int PortIndex,
    // P4.5: concat-bundled port connection (e.g. `.d({z, n, c, v})`). MSB-first
    // list of constituent signal names. Null when the connection is a single
    // signal/sel/const. When present, SignalName is a synthetic placeholder.
    IReadOnlyList<string>? ConcatParts = null);
