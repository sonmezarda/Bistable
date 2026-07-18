namespace Bistable.Engine;

/// <summary>A top-level output value in a simulation frame.</summary>
public sealed record SimulationSignalValue(string Signal, string Value);

/// <summary>Result of a stepping command (SetInput/Eval/Tick/Reset).</summary>
public sealed record SimulationFrameResult(ulong Time, IReadOnlyList<SimulationSignalValue> Signals);

/// <summary>One probe advertised by the worker's probe table.</summary>
public sealed record SimulationProbe(
    string Path,
    int Width,
    bool IsSigned,
    bool IsRegistered,
    bool IsMemory);

/// <summary>A top-level port with the metadata the schematic inspector needs.</summary>
public sealed record SimulationPort(string Name, string Direction, int Width, bool IsSigned);

/// <summary>
/// The state handed back when a simulation session starts: the top module,
/// its ports, the worker's probe catalog, and the initial output frame. The
/// frontend uses this to populate the inspector and seed the value overlay map.
/// </summary>
public sealed record SimulationSessionSnapshot(
    string TopModule,
    IReadOnlyList<SimulationPort> Ports,
    IReadOnlyList<SimulationProbe> Probes,
    SimulationFrameResult InitialFrame);

/// <summary>Per-path outcome of a batched signal read.</summary>
public sealed record SimulationReadOutcome(string Path, string? Value, int Width, bool IsSigned, string? Error);

/// <summary>Result of a batched <c>readSignals</c> request.</summary>
public sealed record SimulationReadResult(IReadOnlyList<SimulationReadOutcome> Results);

/// <summary>
/// Raised when a caller-supplied value fails width/format validation before any
/// worker IPC. Carried as structured RPC error data, not a transport failure.
/// </summary>
public sealed class SimulationValidationException(string message) : Exception(message);
