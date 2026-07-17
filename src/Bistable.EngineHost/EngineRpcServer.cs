using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Bistable.Core.Design;
using Bistable.Engine;
using Bistable.Verilator;

namespace Bistable.EngineHost;

public sealed class EngineRpcServer(DesignElaborationService elaborationService)
{
    private readonly EngineSchematicProjectionService _schematicProjection = new();
    public async Task RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await input.ReadLineAsync(cancellationToken);
            if (line is null) return;
            if (string.IsNullOrWhiteSpace(line)) continue;

            (EngineRpcResponse response, bool shutdown) = await HandleLineAsync(line, cancellationToken);
            await output.WriteLineAsync(JsonSerializer.Serialize(response, EngineRpcProtocol.JsonOptions));
            await output.FlushAsync(cancellationToken);
            if (shutdown) return;
        }
    }

    private async Task<(EngineRpcResponse Response, bool Shutdown)> HandleLineAsync(
        string line,
        CancellationToken cancellationToken)
    {
        EngineRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<EngineRpcRequest>(line, EngineRpcProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            return (Error(string.Empty, "invalid_request", ex.Message), false);
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
        {
            return (Error(request?.Id ?? string.Empty, "invalid_request", "Request id and method are required."), false);
        }

        try
        {
            return request.Method switch
            {
                "hello" => (Success(request.Id, CreateHello()), false),
                "loadProject" => (Success(request.Id, await LoadProjectAsync(request.Params, cancellationToken)), false),
                "shutdown" => (Success(request.Id, new { accepted = true }), true),
                _ => (Error(request.Id, "method_not_found", $"Unknown engine method '{request.Method}'."), false)
            };
        }
        catch (VerilatorInvocationException ex)
        {
            string projectDirectory = GetProjectDirectory(request.Params);
            EngineDiagnostic[] diagnostics = ElaborationDiagnosticsParser
                .Parse(ex.StandardError, projectDirectory)
                .Select(static diagnostic => new EngineDiagnostic(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.FilePath,
                    diagnostic.Line,
                    diagnostic.Column))
                .ToArray();
            return (Error(
                request.Id,
                "elaboration_failed",
                ex.Message,
                new EngineElaborationErrorData(
                    ex.Operation,
                    ex.ExitCode,
                    ex.StandardError,
                    diagnostics)), false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            return (Error(request.Id, "engine_error", ex.Message), false);
        }
    }

    private static EngineHelloResult CreateHello() => new(
        EngineRpcProtocol.Version,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
        ["project.load", "diagnostics.stderr", "schematic.top", "shutdown"]);

    private async Task<EngineProjectSummary> LoadProjectAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("projectPath", out JsonElement pathElement)
            || pathElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(pathElement.GetString()))
        {
            throw new InvalidDataException("loadProject requires params.projectPath.");
        }

        string projectPath = Path.GetFullPath(pathElement.GetString()!);
        Stopwatch stopwatch = Stopwatch.StartNew();
        EngineDesignLoadResult result = await elaborationService.LoadAsync(projectPath, cancellationToken);
        stopwatch.Stop();
        EngineProjectPort[] ports = result.Metadata.Ports
            .OrderBy(static port => port.PinIndex)
            .Select(static port => new EngineProjectPort(
                port.Name,
                port.Direction.ToString(),
                port.Width,
                port.IsSigned))
            .ToArray();
        return new EngineProjectSummary(
            projectPath,
            result.ProjectDirectory,
            result.Metadata.Name,
            result.Ast.Modules.Count,
            ports,
            result.VerilatorVersion,
            stopwatch.Elapsed.TotalMilliseconds,
            _schematicProjection.Project(result.Ast.TopModule
                ?? throw new InvalidDataException("Elaborated AST has no top module.")));
    }

    private static EngineRpcResponse Success(string id, object result) => new(id, result);

    private static string GetProjectDirectory(JsonElement parameters)
    {
        if (parameters.TryGetProperty("projectPath", out JsonElement pathElement)
            && pathElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(pathElement.GetString()))
        {
            string fullPath = Path.GetFullPath(pathElement.GetString()!);
            return Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        }
        return Environment.CurrentDirectory;
    }

    private static EngineRpcResponse Error(string id, string code, string message, object? data = null) =>
        new(id, Error: new EngineRpcError(code, message, data));
}
