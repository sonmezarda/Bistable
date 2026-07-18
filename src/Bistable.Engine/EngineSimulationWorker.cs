using System.Diagnostics;
using Bistable.Protocol;

namespace Bistable.Engine;

/// <summary>
/// Engine-owned transport to a native Verilator worker process. Mirrors the
/// atomic send/drain + protocol-handshake discipline of the Avalonia
/// <c>SimulationWorkerClient</c> so the headless engine host can drive a
/// simulation without a UI dependency. This is worker <em>transport</em> only —
/// no HDL/eval logic lives here; the compiled worker owns all simulation math.
/// </summary>
public sealed class EngineSimulationWorker : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _ioSemaphore = new(1, 1);
    private int _disposeState;
    private long _completedRoundTrips;

    /// <summary>Number of stdin/stdout round-trips completed. Used by budget tests.</summary>
    public long CompletedRoundTrips => Interlocked.Read(ref _completedRoundTrips);

    /// <summary>True once the underlying worker process has exited.</summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private EngineSimulationWorker(string executablePath)
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
    /// Starts a worker and verifies its protocol before handing it to the
    /// caller, so a stale executable can never be attached silently.
    /// </summary>
    public static async Task<EngineSimulationWorker> StartAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        EngineSimulationWorker worker = new(executablePath);
        try
        {
            await worker.EnsureCompatibleProtocolAsync(cancellationToken);
            return worker;
        }
        catch
        {
            await worker.DisposeAsync();
            throw;
        }
    }

    /// <summary>Sends a command and drains exactly one response line.</summary>
    public async Task<WorkerResponse> SendAsync(SimulationCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Simulation worker exited with code {_process.ExitCode}.");
        }

        await _ioSemaphore.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();

            // Once written, the response must always be drained before another
            // caller uses the stream; cancelling the read would desync the pipe.
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

    /// <summary>Stepping commands (SetInput/Eval/Tick/Reset/…) that yield a frame.</summary>
    public async Task<SimulationFrame> StepAsync(SimulationCommand command, CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(command, cancellationToken);
        return response switch
        {
            SimulationFrame frame => frame,
            ErrorResponse err => throw new InvalidOperationException($"{command.Type} failed: {err.Message}"),
            _ => throw new InvalidDataException(
                $"Expected SimulationFrame for {command.Type}, got {response.GetType().Name}")
        };
    }

    /// <summary>
    /// Batch-reads hierarchical signals in chunks of at most
    /// <see cref="WorkerProtocol.MaxSignalsPerBatch"/>. A request at or below
    /// that limit is exactly one round-trip.
    /// </summary>
    public async Task<SignalsReadResult> ReadSignalsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return new SignalsReadResult([]);
        }
        if (paths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Signal paths cannot contain blank values.", nameof(paths));
        }

        List<SignalReadOutcome> results = new(paths.Count);
        for (int offset = 0; offset < paths.Count; offset += WorkerProtocol.MaxSignalsPerBatch)
        {
            string[] chunk = paths
                .Skip(offset)
                .Take(WorkerProtocol.MaxSignalsPerBatch)
                .ToArray();
            WorkerResponse response = await SendAsync(
                new SimulationCommand(SimulationCommandType.ReadSignals, Paths: chunk),
                cancellationToken);
            results.AddRange(Unwrap<SignalsReadResponse>(response, $"ReadSignals({chunk.Length})").Result.Results);
        }
        return new SignalsReadResult(results);
    }

    /// <summary>Enumerate every probe available in the worker's probe table.</summary>
    public async Task<IReadOnlyList<ProbeDescriptor>> ListProbesAsync(CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(
            new SimulationCommand(SimulationCommandType.ListProbes),
            cancellationToken);
        return Unwrap<ProbeListResponse>(response, "ListProbes").Probes;
    }

    private async Task EnsureCompatibleProtocolAsync(CancellationToken cancellationToken)
    {
        WorkerResponse response = await SendAsync(
            new SimulationCommand(SimulationCommandType.Hello),
            cancellationToken);
        WorkerHelloResponse hello = response switch
        {
            WorkerHelloResponse value => value,
            ErrorResponse error => throw new InvalidDataException(
                $"Worker does not support the protocol v{WorkerProtocol.CurrentVersion} handshake ({error.Message}); rebuild the worker."),
            _ => throw new InvalidDataException(
                $"Expected WorkerHelloResponse for Hello, got {response.GetType().Name}; rebuild the worker.")
        };
        if (hello.ProtocolVersion != WorkerProtocol.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Worker protocol v{hello.ProtocolVersion} is incompatible with engine protocol v{WorkerProtocol.CurrentVersion}; rebuild the worker.");
        }
        if (!hello.Capabilities.Contains(WorkerProtocol.ReadSignalsCapability, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Worker protocol v{hello.ProtocolVersion} does not advertise '{WorkerProtocol.ReadSignalsCapability}'; rebuild the worker.");
        }
    }

    private static T Unwrap<T>(WorkerResponse response, string operation) where T : WorkerResponse =>
        response switch
        {
            T match => match,
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
