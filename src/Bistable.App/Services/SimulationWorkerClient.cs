using System.Diagnostics;
using Bistable.Protocol;

namespace Bistable.App.Services;

public sealed class SimulationWorkerClient : IAsyncDisposable
{
    private readonly Process _process;

    public SimulationWorkerClient(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start simulation worker: {executablePath}");
        }
    }

    /// <summary>
    /// Sends a command and returns the worker's response. The response is one
    /// of the <see cref="WorkerResponse"/> subtypes — pattern-match on the
    /// concrete type to read the payload, or use the typed wrappers below.
    /// </summary>
    private readonly SemaphoreSlim _ioSemaphore = new(1, 1);

    public async Task<WorkerResponse> SendAsync(SimulationCommand command, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Simulation worker exited with code {_process.ExitCode}.");
        }

        // Serialize stdin write + stdout read so concurrent callers (e.g. a
        // fire-and-forget descriptor refresh racing with the UI's next eval)
        // don't interleave their commands and garble responses.
        await _ioSemaphore.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(ProtocolJson.Serialize(command).AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);

            string? line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Simulation worker closed stdout. {stderr}");
            }

            return ProtocolJson.Deserialize<WorkerResponse>(line)
                ?? throw new InvalidDataException("Simulation worker returned an invalid response.");
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    /// <summary>
    /// Convenience for stepping commands (Eval/Tick/RunCycles/SetInput/Reset/
    /// Pause/GetSnapshot) which always produce a <see cref="SimulationFrame"/>.
    /// </summary>
    public async Task<SimulationFrame> StepAsync(SimulationCommand command, CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(command, cancellationToken);
        return response switch
        {
            SimulationFrame frame => frame,
            ErrorResponse err     => throw new InvalidOperationException($"{command.Type} failed: {err.Message}"),
            _ => throw new InvalidDataException(
                $"Expected SimulationFrame for {command.Type}, got {response.GetType().Name}")
        };
    }

    // ── Typed probe wrappers ─────────────────────────────────────────────

    /// <summary>
    /// Read the live value of an internal hierarchical signal (e.g.
    /// <c>"arnicomp_top.acc.q"</c>). Returns the value mid-simulation without
    /// requiring a VCD round-trip.
    /// </summary>
    public async Task<SignalReadResult> ReadSignalAsync(string hierarchyPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyPath);
        WorkerResponse response = await SendAsync(
            new SimulationCommand(SimulationCommandType.ReadSignal, Path: hierarchyPath),
            cancellationToken);
        return Unwrap<SignalReadResponse>(response, $"ReadSignal('{hierarchyPath}')").Result;
    }

    /// <summary>One-shot write to an internal signal. Simulation may overwrite on next Eval.</summary>
    public Task WriteSignalAsync(string hierarchyPath, string value, CancellationToken cancellationToken) =>
        SendAckedAsync(SimulationCommandType.WriteSignal, hierarchyPath, value, cancellationToken);

    /// <summary>
    /// Pin a signal to a value across subsequent <c>Eval</c> calls. The worker
    /// re-applies the forced value at the top of every Eval until
    /// <see cref="ReleaseSignalAsync"/> clears it.
    /// </summary>
    public Task ForceSignalAsync(string hierarchyPath, string value, CancellationToken cancellationToken) =>
        SendAckedAsync(SimulationCommandType.ForceSignal, hierarchyPath, value, cancellationToken);

    /// <summary>Clears a prior <see cref="ForceSignalAsync"/>.</summary>
    public Task ReleaseSignalAsync(string hierarchyPath, CancellationToken cancellationToken) =>
        SendAckedAsync(SimulationCommandType.ReleaseSignal, hierarchyPath, value: null, cancellationToken);

    /// <summary>Read a contiguous range of memory cells starting at <paramref name="address"/>.</summary>
    public async Task<MemoryReadResult> ReadMemoryAsync(
        string hierarchyPath, ulong address, int count, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyPath);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be positive.");
        WorkerResponse response = await SendAsync(
            new SimulationCommand(
                SimulationCommandType.ReadMemory,
                Path: hierarchyPath,
                MemoryAddress: address,
                MemoryCount: count),
            cancellationToken);
        return Unwrap<MemoryReadResponse>(response, $"ReadMemory('{hierarchyPath}', {address}, {count})").Result;
    }

    /// <summary>Write a single memory cell.</summary>
    public async Task WriteMemoryAsync(
        string hierarchyPath, ulong address, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyPath);
        WorkerResponse response = await SendAsync(
            new SimulationCommand(
                SimulationCommandType.WriteMemory,
                Path: hierarchyPath,
                Value: value,
                MemoryAddress: address),
            cancellationToken);
        Unwrap<AckResponse>(response, $"WriteMemory('{hierarchyPath}', {address})");
    }

    /// <summary>Enumerate every probe available in the worker's probe table.</summary>
    public async Task<IReadOnlyList<ProbeDescriptor>> ListProbesAsync(CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(
            new SimulationCommand(SimulationCommandType.ListProbes),
            cancellationToken);
        return Unwrap<ProbeListResponse>(response, "ListProbes").Probes;
    }

    private async Task SendAckedAsync(
        SimulationCommandType type, string hierarchyPath, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyPath);
        WorkerResponse response = await SendAsync(
            new SimulationCommand(type, Path: hierarchyPath, Value: value),
            cancellationToken);
        Unwrap<AckResponse>(response, $"{type}('{hierarchyPath}')");
    }

    /// <summary>
    /// Casts the worker response to the expected concrete type, surfacing
    /// <see cref="ErrorResponse"/> as a structured exception and any other
    /// unexpected type as a protocol violation.
    /// </summary>
    private static T Unwrap<T>(WorkerResponse response, string operation) where T : WorkerResponse =>
        response switch
        {
            T match           => match,
            ErrorResponse err => throw new InvalidOperationException($"{operation} failed: {err.Message}"),
            _ => throw new InvalidDataException(
                $"Expected {typeof(T).Name} for {operation}, got {response.GetType().Name}")
        };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync(ProtocolJson.Serialize(new SimulationCommand(SimulationCommandType.Pause)));
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _ioSemaphore.Dispose();
        }
    }
}
