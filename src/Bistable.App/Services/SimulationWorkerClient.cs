using System.Diagnostics;
using Bistable.Protocol;

namespace Bistable.App.Services;

public sealed class SimulationWorkerClient : IAsyncDisposable
{
    private readonly Process _process;
    private int _disposeState;
    private long _completedRoundTrips;

    internal long CompletedRoundTrips => Interlocked.Read(ref _completedRoundTrips);

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
    /// Starts a worker and verifies its protocol before returning it to the
    /// caller. Build flows use this entry point so stale executables cannot be
    /// attached to the live UI accidentally.
    /// </summary>
    public static async Task<SimulationWorkerClient> StartAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        SimulationWorkerClient client = new(executablePath);
        try
        {
            await client.EnsureCompatibleProtocolAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();

            // Once a command is written, its response must always be drained before
            // another caller can use the stream. Cancelling the read would release
            // the semaphore while the worker's response is still pending, causing
            // the next command to consume the wrong response.
            await _process.StandardInput.WriteLineAsync(
                ProtocolJson.Serialize(command).AsMemory(),
                CancellationToken.None);
            await _process.StandardInput.FlushAsync(CancellationToken.None);

            string? line = await _process.StandardOutput.ReadLineAsync(CancellationToken.None);
            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(CancellationToken.None);
                throw new InvalidOperationException($"Simulation worker closed stdout. {stderr}");
            }

            WorkerResponse response = ProtocolJson.Deserialize<WorkerResponse>(line)
                ?? throw new InvalidDataException("Simulation worker returned an invalid response.");
            Interlocked.Increment(ref _completedRoundTrips);
            cancellationToken.ThrowIfCancellationRequested();
            return response;
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

    /// <summary>Returns the worker's protocol version and advertised capabilities.</summary>
    public async Task<WorkerHelloResponse> HelloAsync(CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(
            new SimulationCommand(SimulationCommandType.Hello),
            cancellationToken);
        return response switch
        {
            WorkerHelloResponse hello => hello,
            ErrorResponse error => throw new InvalidDataException(
                $"Worker does not support the protocol v{WorkerProtocol.CurrentVersion} handshake ({error.Message}); rebuild the worker."),
            _ => throw new InvalidDataException(
                $"Expected WorkerHelloResponse for Hello, got {response.GetType().Name}; rebuild the worker.")
        };
    }

    /// <summary>Rejects a worker built for a different protocol generation.</summary>
    public async Task EnsureCompatibleProtocolAsync(CancellationToken cancellationToken)
    {
        WorkerHelloResponse hello = await HelloAsync(cancellationToken);
        if (hello.ProtocolVersion != WorkerProtocol.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Worker protocol v{hello.ProtocolVersion} is incompatible with GUI protocol v{WorkerProtocol.CurrentVersion}; rebuild the worker.");
        }
        if (!hello.Capabilities.Contains(WorkerProtocol.ReadSignalsCapability, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Worker protocol v{hello.ProtocolVersion} does not advertise '{WorkerProtocol.ReadSignalsCapability}'; rebuild the worker.");
        }
    }

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

    /// <summary>
    /// Reads hierarchical signals in batches of at most
    /// <see cref="WorkerProtocol.MaxSignalsPerBatch"/>. Requests at or below
    /// that limit use exactly one command/response round-trip.
    /// </summary>
    public async Task<SignalsReadResult> ReadSignalsAsync(
        IEnumerable<string> hierarchyPaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hierarchyPaths);
        string[] paths = hierarchyPaths.ToArray();
        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Signal paths cannot contain blank values.", nameof(hierarchyPaths));
        }
        if (paths.Length == 0)
        {
            return new SignalsReadResult([]);
        }

        List<SignalReadOutcome> results = new(paths.Length);
        for (int offset = 0; offset < paths.Length; offset += WorkerProtocol.MaxSignalsPerBatch)
        {
            string[] chunk = paths[offset..Math.Min(paths.Length, offset + WorkerProtocol.MaxSignalsPerBatch)];
            WorkerResponse response = await SendAsync(
                new SimulationCommand(SimulationCommandType.ReadSignals, Paths: chunk),
                cancellationToken);
            results.AddRange(Unwrap<SignalsReadResponse>(response, $"ReadSignals({chunk.Length})").Result.Results);
        }
        return new SignalsReadResult(results);
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        await _ioSemaphore.WaitAsync(CancellationToken.None);
        try
        {
            _process.Dispose();
        }
        finally
        {
            _ioSemaphore.Release();
            _ioSemaphore.Dispose();
        }
    }
}
