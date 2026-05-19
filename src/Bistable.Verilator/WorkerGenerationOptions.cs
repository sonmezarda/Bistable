namespace Bistable.Verilator;

public sealed record WorkerGenerationOptions(
    string? DefaultClock,
    string? ResetSignal,
    int ResetActiveLevel,
    bool TraceEnabled,
    int TraceDepth,
    string TraceFileName);
