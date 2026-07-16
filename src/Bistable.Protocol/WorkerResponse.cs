using System.Text.Json.Serialization;

namespace Bistable.Protocol;

/// <summary>
/// One response frame from the worker. Discriminated union — every command
/// produces exactly one subtype. The <c>kind</c> field on the wire selects the
/// concrete type at deserialization time.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WorkerHelloResponse),  "hello")]
[JsonDerivedType(typeof(SimulationFrame),    "frame")]
[JsonDerivedType(typeof(SignalReadResponse), "signalRead")]
[JsonDerivedType(typeof(SignalsReadResponse), "signalsRead")]
[JsonDerivedType(typeof(MemoryReadResponse), "memoryRead")]
[JsonDerivedType(typeof(ProbeListResponse),  "probeList")]
[JsonDerivedType(typeof(AckResponse),        "ack")]
[JsonDerivedType(typeof(ErrorResponse),      "error")]
public abstract record WorkerResponse;

/// <summary>Response to <see cref="SimulationCommandType.Hello"/>.</summary>
public sealed record WorkerHelloResponse(
    int ProtocolVersion,
    IReadOnlyList<string> Capabilities) : WorkerResponse;

/// <summary>
/// Result of any simulation-stepping command (<c>Eval</c>, <c>Tick</c>,
/// <c>RunCycles</c>, <c>SetInput</c>, <c>Reset</c>, <c>Pause</c>,
/// <c>GetSnapshot</c>). Carries the current top-level port values plus any
/// trace events accumulated since the last frame.
/// </summary>
public sealed record SimulationFrame(
    ulong Time,
    IReadOnlyList<SignalSample> Signals,
    IReadOnlyList<SignalSample>? Trace = null) : WorkerResponse;

/// <summary>Response to <see cref="SimulationCommandType.ReadSignal"/>.</summary>
public sealed record SignalReadResponse(SignalReadResult Result) : WorkerResponse;

/// <summary>Response to <see cref="SimulationCommandType.ReadSignals"/>.</summary>
public sealed record SignalsReadResponse(SignalsReadResult Result) : WorkerResponse;

/// <summary>Response to <see cref="SimulationCommandType.ReadMemory"/>.</summary>
public sealed record MemoryReadResponse(MemoryReadResult Result) : WorkerResponse;

/// <summary>Response to <see cref="SimulationCommandType.ListProbes"/>.</summary>
public sealed record ProbeListResponse(IReadOnlyList<ProbeDescriptor> Probes) : WorkerResponse;

/// <summary>
/// Bare acknowledgement — returned by side-effecting commands
/// (<see cref="SimulationCommandType.WriteSignal"/>,
/// <see cref="SimulationCommandType.ForceSignal"/>,
/// <see cref="SimulationCommandType.ReleaseSignal"/>,
/// <see cref="SimulationCommandType.WriteMemory"/>) that have no payload to
/// return.
/// </summary>
public sealed record AckResponse : WorkerResponse;

/// <summary>
/// Worker-side error: unknown probe path, out-of-range memory access, probe
/// table disabled, command parse failure, or any internal exception caught in
/// the dispatch loop.
/// </summary>
public sealed record ErrorResponse(string Message) : WorkerResponse;
