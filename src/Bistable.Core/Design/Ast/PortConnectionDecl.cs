namespace Bistable.Core.Design.Ast;

public sealed record PortConnectionDecl(
    string PortName,
    string SignalName,
    string Direction,
    int PortIndex,
    // P2-11: when the connection is a bit-slice of a wider signal (e.g.
    // `.ops(control_pins.ops)` becomes `<sel><varref name="control_pins"/>...</sel>`
    // in Verilator XML), the range carries the slice [hi:lo]. Null for direct
    // (non-sliced) connections. The decoder uses this to detect struct field
    // accesses and group them into a fan-out primitive.
    BitRange? SignalRange = null,
    // P4.5: concat-bundled port connection (e.g. `.d({z, n, c, v})`). Holds the
    // flat MSB-first list of constituent signal names. When present, SignalName
    // is a synthetic placeholder ("?") because no single signal drives the port.
    IReadOnlyList<string>? ConcatParts = null);
