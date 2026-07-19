using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.Engine;

namespace Bistable.EngineHost;

public static class EngineRpcProtocol
{
    // v2 adds the simulation.* method family (start/setInput/eval/tick/reset/
    // readSignals/stop). A frontend built for a different generation is rejected
    // at the hello handshake.
    public const int Version = 2;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record EngineRpcRequest(string Id, string Method, JsonElement Params);

public sealed record EngineRpcResponse(string Id, object? Result = null, EngineRpcError? Error = null);

public sealed record EngineRpcError(string Code, string Message, object? Data = null);

public sealed record EngineHelloResult(
    int ProtocolVersion,
    string EngineVersion,
    IReadOnlyList<string> Capabilities);

public sealed record EngineProjectPort(string Name, string Direction, int Width, bool IsSigned);

public sealed record EngineDiagnostic(
    string Severity,
    string? Code,
    string Message,
    string FilePath,
    int Line,
    int Column);

public sealed record EngineElaborationErrorData(
    string Operation,
    int ExitCode,
    string StandardError,
    IReadOnlyList<EngineDiagnostic> Diagnostics);

public sealed record EngineProjectSummary(
    string ProjectPath,
    string ProjectDirectory,
    string TopModule,
    int ModuleCount,
    IReadOnlyList<EngineProjectPort> Ports,
    string VerilatorVersion,
    double ElapsedMs,
    EngineSchematicGraph Schematic);

/// <summary>
/// Result of <c>loadModuleSchematic</c>: the exact instance path echoed back as
/// the document identity, the resolved module type for display, and its
/// layout-agnostic schematic graph.
/// </summary>
public sealed record EngineModuleSchematicResult(
    string InstancePath,
    string ModuleName,
    EngineSchematicGraph Schematic);

// ── simulation.* (protocol v2) ───────────────────────────────────────────

public sealed record EngineSimulationSignal(string Signal, string Value);

public sealed record EngineSimulationFrame(ulong Time, IReadOnlyList<EngineSimulationSignal> Signals);

public sealed record EngineSimulationProbe(
    string Path,
    int Width,
    bool IsSigned,
    bool IsRegistered,
    bool IsMemory);

public sealed record EngineSimulationSnapshot(
    string TopModule,
    IReadOnlyList<EngineProjectPort> Ports,
    IReadOnlyList<EngineSimulationProbe> Probes,
    EngineSimulationFrame InitialFrame);

public sealed record EngineSimulationReadOutcome(
    string Path,
    string? Value,
    int Width,
    bool IsSigned,
    string? Error);

public sealed record EngineSimulationReadResult(IReadOnlyList<EngineSimulationReadOutcome> Results);
