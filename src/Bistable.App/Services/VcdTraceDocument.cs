namespace Bistable.App.Services;

public sealed record VcdTraceSignal(
    string Name,
    string ShortName,
    string ScopePath,
    int Width,
    bool IsTopLevel);

public sealed record VcdTraceEvent(long Order, ulong Time, string SignalName, string Value);

public sealed record VcdTraceDocument(
    IReadOnlyList<VcdTraceSignal> Signals,
    IReadOnlyDictionary<string, IReadOnlyList<VcdTraceEvent>> EventsBySignal,
    long MaxOrder,
    ulong MaxTime)
{
    public static VcdTraceDocument Empty { get; } = new(
        Array.Empty<VcdTraceSignal>(),
        new Dictionary<string, IReadOnlyList<VcdTraceEvent>>(StringComparer.OrdinalIgnoreCase),
        0,
        0);

    public bool TryGetEvents(string signalName, out IReadOnlyList<VcdTraceEvent> events) =>
        EventsBySignal.TryGetValue(signalName, out events!);
}
