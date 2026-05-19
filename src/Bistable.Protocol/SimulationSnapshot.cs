namespace Bistable.Protocol;

public sealed record SimulationSnapshot(
    ulong Time,
    IReadOnlyList<SignalSample> Signals,
    IReadOnlyList<SignalSample>? Trace = null);
