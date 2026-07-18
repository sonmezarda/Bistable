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
    private readonly SimulationSessionService _simulation = new();
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
                "simulation.start" => (Success(request.Id, await SimulationStartAsync(request.Params, cancellationToken)), false),
                "simulation.setInput" => (Success(request.Id, await SimulationSetInputAsync(request.Params, cancellationToken)), false),
                "simulation.eval" => (Success(request.Id, ToFrame(await _simulation.EvalAsync(cancellationToken))), false),
                "simulation.tick" => (Success(request.Id, ToFrame(await _simulation.TickAsync(GetOptionalString(request.Params, "clock"), cancellationToken))), false),
                "simulation.reset" => (Success(request.Id, ToFrame(await _simulation.ResetAsync(cancellationToken))), false),
                "simulation.readSignals" => (Success(request.Id, await SimulationReadSignalsAsync(request.Params, cancellationToken)), false),
                "simulation.stop" => (Success(request.Id, await SimulationStopAsync()), false),
                "shutdown" => (await ShutdownAsync(request.Id), true),
                _ => (Error(request.Id, "method_not_found", $"Unknown engine method '{request.Method}'."), false)
            };
        }
        catch (SimulationValidationException ex)
        {
            return (Error(request.Id, "invalid_value", ex.Message), false);
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
        [
            "project.load", "diagnostics.stderr", "schematic.top", "shutdown",
            "simulation.start", "simulation.step", "simulation.readSignals"
        ]);

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

    private async Task<EngineSimulationSnapshot> SimulationStartAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        string projectPath = RequireProjectPath(parameters);
        EngineDesignLoadResult design = await elaborationService.LoadAsync(projectPath, cancellationToken);
        SimulationSessionSnapshot snapshot = await _simulation.StartAsync(design, cancellationToken);
        return new EngineSimulationSnapshot(
            snapshot.TopModule,
            snapshot.Ports.Select(static p => new EngineProjectPort(p.Name, p.Direction, p.Width, p.IsSigned)).ToArray(),
            snapshot.Probes.Select(static p => new EngineSimulationProbe(p.Path, p.Width, p.IsSigned, p.IsRegistered, p.IsMemory)).ToArray(),
            ToFrame(snapshot.InitialFrame));
    }

    private async Task<EngineSimulationFrame> SimulationSetInputAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        string signal = GetOptionalString(parameters, "signal")
            ?? throw new SimulationValidationException("simulation.setInput requires params.signal.");
        string? value = GetOptionalString(parameters, "value");
        return ToFrame(await _simulation.SetInputAsync(signal, value, cancellationToken));
    }

    private async Task<EngineSimulationReadResult> SimulationReadSignalsAsync(
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        string[] paths = [];
        if (parameters.TryGetProperty("paths", out JsonElement pathsElement)
            && pathsElement.ValueKind == JsonValueKind.Array)
        {
            paths = pathsElement.EnumerateArray()
                .Where(static e => e.ValueKind == JsonValueKind.String)
                .Select(static e => e.GetString()!)
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        SimulationReadResult result = await _simulation.ReadSignalsAsync(paths, cancellationToken);
        return new EngineSimulationReadResult(result.Results
            .Select(static r => new EngineSimulationReadOutcome(r.Path, r.Value, r.Width, r.IsSigned, r.Error))
            .ToArray());
    }

    private async Task<object> SimulationStopAsync()
    {
        await _simulation.DisposeAsync();
        return new { accepted = true };
    }

    private async Task<EngineRpcResponse> ShutdownAsync(string id)
    {
        await _simulation.DisposeAsync();
        return Success(id, new { accepted = true });
    }

    private static EngineSimulationFrame ToFrame(SimulationFrameResult frame) => new(
        frame.Time,
        frame.Signals.Select(static s => new EngineSimulationSignal(s.Signal, s.Value)).ToArray());

    private static string RequireProjectPath(JsonElement parameters)
    {
        string? path = GetOptionalString(parameters, "projectPath");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Request requires params.projectPath.");
        }
        return Path.GetFullPath(path);
    }

    private static string? GetOptionalString(JsonElement parameters, string property) =>
        parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty(property, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

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
