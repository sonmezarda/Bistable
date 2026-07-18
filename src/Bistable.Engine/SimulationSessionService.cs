using Bistable.Core.Design;
using Bistable.Core.Projects;
using Bistable.Protocol;
using Bistable.Verilator;

namespace Bistable.Engine;

/// <summary>
/// Owns the native Verilator worker for one loaded project and exposes the live
/// simulation loop (drive input → eval/tick/reset → batched read) to headless
/// frontends. UI-independent: the Theia workbench reaches this only through the
/// engine host. All simulation math stays in the compiled worker; this service
/// is lifecycle + validation + transport orchestration.
/// </summary>
/// <remarks>
/// Every started worker gets a monotonically increasing <em>generation</em>.
/// A project reload starts a new generation, swaps the worker atomically, and
/// disposes the previous one — so a late frame or read from a superseded
/// generation is dropped rather than applied to the new session.
/// </remarks>
public sealed class SimulationSessionService : IAsyncDisposable
{
    private readonly SimulationWorkerBuilder _builder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EngineSimulationWorker? _worker;
    private ModuleMetadata? _metadata;
    private Dictionary<string, SimulationPort> _portsByName = new(StringComparer.Ordinal);
    private long _generation;
    private int _disposeState;

    public SimulationSessionService(SimulationWorkerBuilder? builder = null)
    {
        _builder = builder ?? new SimulationWorkerBuilder();
    }

    /// <summary>The current session generation. Increments on every successful start.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>True while a worker is attached. False once disposed.</summary>
    public bool HasWorker
    {
        get
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return false;
            }
            _gate.Wait();
            try
            {
                return _worker is not null && !_worker.HasExited;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Round-trips completed by the active worker (0 when none). Budget tests read this.</summary>
    public long CompletedRoundTrips
    {
        get
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                return 0;
            }
            _gate.Wait();
            try
            {
                return _worker?.CompletedRoundTrips ?? 0;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// Builds and starts a worker for the elaborated design, performs the initial
    /// Eval, and returns the session snapshot. Disposes any previous worker.
    /// </summary>
    public async Task<SimulationSessionSnapshot> StartAsync(
        EngineDesignLoadResult design,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(design);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        // Build + start the replacement worker OUTSIDE the swap lock so a long
        // Verilator build does not block reads of the still-live old worker.
        SimulationWorkerBuildResult build = await _builder.BuildAsync(
            design.Project,
            design.Metadata,
            design.ProjectDirectory,
            cancellationToken,
            progress: null,
            designAst: design.Ast);
        EngineSimulationWorker worker = await EngineSimulationWorker.StartAsync(
            build.ExecutablePath,
            cancellationToken);

        IReadOnlyList<ProbeDescriptor> descriptors;
        SimulationFrame initialFrame;
        try
        {
            descriptors = await worker.ListProbesAsync(cancellationToken);
            initialFrame = await worker.StepAsync(
                new SimulationCommand(SimulationCommandType.Eval),
                cancellationToken);
        }
        catch
        {
            await worker.DisposeAsync();
            throw;
        }

        EngineSimulationWorker? previous;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            previous = _worker;
            _worker = worker;
            _metadata = design.Metadata;
            _portsByName = design.Metadata.Ports.ToDictionary(
                static p => p.Name,
                static p => new SimulationPort(p.Name, p.Direction.ToString(), p.Width, p.IsSigned),
                StringComparer.Ordinal);
            Interlocked.Increment(ref _generation);
        }
        finally
        {
            _gate.Release();
        }

        if (previous is not null)
        {
            await previous.DisposeAsync();
        }

        SimulationPort[] ports = design.Metadata.Ports
            .OrderBy(static p => p.PinIndex)
            .Select(static p => new SimulationPort(p.Name, p.Direction.ToString(), p.Width, p.IsSigned))
            .ToArray();
        SimulationProbe[] probes = descriptors
            .Select(static d => new SimulationProbe(d.Path, d.Width, d.IsSigned, d.IsRegistered, d.IsMemory))
            .ToArray();
        return new SimulationSessionSnapshot(design.Metadata.Name, ports, probes, ToFrame(initialFrame));
    }

    /// <summary>
    /// Validates the value against the port width, writes it, and evals. Throws
    /// <see cref="SimulationValidationException"/> before any IPC on a bad value.
    /// </summary>
    public async Task<SimulationFrameResult> SetInputAsync(
        string signal,
        string? value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        (EngineSimulationWorker worker, ModuleMetadata metadata) = RequireWorker();
        if (!_portsByName.TryGetValue(signal, out SimulationPort? port))
        {
            throw new SimulationValidationException($"Unknown port '{signal}'.");
        }
        if (!string.Equals(port.Direction, SignalDirection.Input.ToString(), StringComparison.Ordinal)
            && !string.Equals(port.Direction, SignalDirection.InOut.ToString(), StringComparison.Ordinal))
        {
            throw new SimulationValidationException($"Port '{signal}' is not drivable (direction {port.Direction}).");
        }

        SimulationValueValidation validation = SimulationValueValidator.Validate(value, port.Width);
        if (!validation.IsValid)
        {
            // Reject BEFORE touching the worker — the compiled process never sees a bad value.
            throw new SimulationValidationException(validation.Error!);
        }

        SimulationFrame frame = await worker.StepAsync(
            new SimulationCommand(SimulationCommandType.SetInput, signal, validation.NormalizedValue),
            cancellationToken);
        _ = metadata;
        return ToFrame(frame);
    }

    public Task<SimulationFrameResult> EvalAsync(CancellationToken cancellationToken) =>
        StepAsync(new SimulationCommand(SimulationCommandType.Eval), cancellationToken);

    public Task<SimulationFrameResult> TickAsync(string? clock, CancellationToken cancellationToken)
    {
        string clockSignal = clock
            ?? _metadata?.Ports.FirstOrDefault(static p => p.Direction == SignalDirection.Input && p.IsScalar)?.Name
            ?? throw new SimulationValidationException("No clock signal is configured for this design.");
        return StepAsync(new SimulationCommand(SimulationCommandType.Tick, Signal: clockSignal), cancellationToken);
    }

    public Task<SimulationFrameResult> ResetAsync(CancellationToken cancellationToken) =>
        StepAsync(new SimulationCommand(SimulationCommandType.Reset), cancellationToken);

    /// <summary>One batched read of every requested path (chunked past 4K).</summary>
    public async Task<SimulationReadResult> ReadSignalsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        (EngineSimulationWorker worker, _) = RequireWorker();
        SignalsReadResult batch = await worker.ReadSignalsAsync(paths, cancellationToken);
        SimulationReadOutcome[] outcomes = batch.Results
            .Select(static r => new SimulationReadOutcome(r.Path, r.Value, r.Width, r.IsSigned, r.Error))
            .ToArray();
        return new SimulationReadResult(outcomes);
    }

    private async Task<SimulationFrameResult> StepAsync(
        SimulationCommand command,
        CancellationToken cancellationToken)
    {
        (EngineSimulationWorker worker, _) = RequireWorker();
        SimulationFrame frame = await worker.StepAsync(command, cancellationToken);
        return ToFrame(frame);
    }

    private (EngineSimulationWorker Worker, ModuleMetadata Metadata) RequireWorker()
    {
        _gate.Wait();
        try
        {
            if (_worker is null || _metadata is null)
            {
                throw new InvalidOperationException("No simulation session is active. Call StartAsync first.");
            }
            return (_worker, _metadata);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SimulationFrameResult ToFrame(SimulationFrame frame) => new(
        frame.Time,
        frame.Signals.Select(static s => new SimulationSignalValue(s.Signal, s.Value)).ToArray());

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        EngineSimulationWorker? worker;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            worker = _worker;
            _worker = null;
        }
        finally
        {
            _gate.Release();
        }
        if (worker is not null)
        {
            await worker.DisposeAsync();
        }
        _gate.Dispose();
    }
}
