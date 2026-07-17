using System.Text.Json;
using System.Text.Json.Serialization;
using Bistable.Engine;

namespace Bistable.EngineHost;

public static class EngineRpcProtocol
{
    public const int Version = 1;

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
