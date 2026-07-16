using Bistable.Protocol;

namespace Bistable.App.Services;

/// <summary>
/// Bridges the GUI to the worker's Phase 3 probe table. Holds a small in-memory
/// cache of recently-read signal values so the UI can render synchronously
/// without blocking on the worker IPC; readers kick async refreshes whose
/// completion fires <see cref="ValueUpdated"/> for a single read or
/// <see cref="ValuesUpdated"/> once for a frame batch.
/// </summary>
/// <remarks>
/// The service is process-singleton from the GUI's perspective (one
/// <see cref="MainWindowViewModel"/> owns it) but reseats its
/// <see cref="SimulationWorkerClient"/> whenever the active worker changes
/// (e.g. project re-build, enter/exit sub-sim). Cache is cleared on reseat
/// and on <see cref="InvalidateAll"/> — the latter is the call that runs
/// after every Eval/Tick to mark every cached value as potentially stale.
/// </remarks>
public sealed class LiveProbeService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MemorySnapshot> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProbeDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private SimulationWorkerClient? _worker;
    private long _workerGeneration;

    /// <summary>Raised on the calling task's context whenever a path's value changes from a prior cached value.</summary>
    public event EventHandler<ProbeValueUpdatedEventArgs>? ValueUpdated;

    /// <summary>
    /// Raised once after a batch refresh when one or more cached scalar values
    /// changed. This keeps a visible frame refresh to one UI invalidation.
    /// </summary>
    public event EventHandler<ProbeValuesUpdatedEventArgs>? ValuesUpdated;

    /// <summary>Raised whenever a memory snapshot finishes refreshing (any cell changed OR first read).</summary>
    public event EventHandler<MemorySnapshotUpdatedEventArgs>? MemoryUpdated;

    /// <summary>True when an active worker is attached; false during build / between projects.</summary>
    public bool HasWorker
    {
        get { lock (_gate) return _worker is not null; }
    }

    /// <summary>
    /// Reseat the worker. Pass <c>null</c> to detach (e.g. before disposing).
    /// Clears the cache so stale values from the previous worker can't leak.
    /// </summary>
    public void AttachWorker(SimulationWorkerClient? worker)
    {
        lock (_gate)
        {
            _worker = worker;
            _workerGeneration++;
            _cache.Clear();
            _memoryCache.Clear();
            _descriptors.Clear();
        }
    }

    /// <summary>
    /// Pull the worker's probe descriptor list and cache it for IsMemory /
    /// MemoryDepth lookups. Fire-and-forget from <see cref="MainWindowViewModel"/>
    /// right after a worker is attached.
    /// </summary>
    public async Task RefreshDescriptorsAsync(CancellationToken cancellationToken)
    {
        (SimulationWorkerClient? worker, long generation) = CaptureWorker();
        if (worker is null) return;

        IReadOnlyList<ProbeDescriptor> descriptors;
        try
        {
            descriptors = await worker.ListProbesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        lock (_gate)
        {
            if (!IsCurrentWorker(worker, generation)) return;
            _descriptors.Clear();
            foreach (ProbeDescriptor d in descriptors)
            {
                _descriptors[d.Path] = d;
            }
        }
    }

    /// <summary>
    /// Write a single memory cell. After the write succeeds, the snapshot
    /// cache is invalidated so the next read picks up the new value.
    /// </summary>
    public async Task<bool> WriteMemoryCellAsync(string path, ulong address, string value, CancellationToken cancellationToken)
    {
        (SimulationWorkerClient? worker, long generation) = CaptureWorker();
        if (worker is null) return false;
        try
        {
            await worker.WriteMemoryAsync(path, address, value, cancellationToken);
            lock (_gate)
            {
                if (!IsCurrentWorker(worker, generation)) return false;
                _memoryCache.Remove(path);
            }
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// P4-1 helper: re-read every scalar probe from the worker so the cache
    /// has fresh values for the next schematic draw. Memory probes are
    /// skipped (they'd be huge and the renderer doesn't show their content
    /// inline yet). Errors per path are swallowed — best-effort population.
    /// </summary>
    public async Task RefreshAllScalarsAsync(CancellationToken cancellationToken)
    {
        ProbeDescriptor[] descriptors;
        lock (_gate)
        {
            descriptors = _descriptors.Values.Where(d => !d.IsMemory).ToArray();
        }
        await RefreshScalarPathsCoreAsync(descriptors.Select(d => d.Path), cancellationToken);
    }

    /// <summary>
    /// P4-5: refresh only the explicitly-listed scalar probe paths instead of
    /// every probe in the catalog. Used by <see cref="MainWindowViewModel"/>
    /// after Tick/Eval when the schematic renderer has reported which paths
    /// it actually touched in the last frame (memory probes / off-screen FFs
    /// / collapsed compounds are skipped).
    /// </summary>
    public Task RefreshScalarsAsync(IEnumerable<string> paths, CancellationToken cancellationToken) =>
        RefreshScalarPathsCoreAsync(paths, cancellationToken);

    private async Task RefreshScalarPathsCoreAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        (SimulationWorkerClient? worker, long generation) = CaptureWorker();
        if (worker is null) return;
        if (cancellationToken.IsCancellationRequested) return;

        string[] requested = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        List<string> scalarPaths = new(requested.Length);
        lock (_gate)
        {
            if (!IsCurrentWorker(worker, generation)) return;
            foreach (string path in requested)
            {
                if (_descriptors.TryGetValue(path, out ProbeDescriptor? descriptor)
                    && !descriptor.IsMemory)
                {
                    // Worker lookup is case-sensitive; send the canonical path
                    // advertised by ListProbes even though the UI cache accepts
                    // case-insensitive references.
                    scalarPaths.Add(descriptor.Path);
                }
            }
        }
        if (scalarPaths.Count == 0) return;

        SignalsReadResult batch;
        try
        {
            batch = await worker.ReadSignalsAsync(scalarPaths, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        List<SignalReadOutcome> changed = [];
        lock (_gate)
        {
            if (!IsCurrentWorker(worker, generation)) return;
            foreach (SignalReadOutcome result in batch.Results)
            {
                if (!result.IsSuccess || result.Value is null) continue;
                bool valueChanged = !_cache.TryGetValue(result.Path, out string? prior)
                    || !string.Equals(prior, result.Value, StringComparison.Ordinal);
                _cache[result.Path] = result.Value;
                if (valueChanged)
                {
                    changed.Add(result);
                }
            }
        }
        if (changed.Count > 0 && IsCurrentWorkerSnapshot(worker, generation))
        {
            ValuesUpdated?.Invoke(this, new ProbeValuesUpdatedEventArgs(changed));
        }
    }

    /// <summary>Returns the probe descriptor for <paramref name="path"/>, or <c>null</c> if not in the catalog.</summary>
    public ProbeDescriptor? GetDescriptor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (_gate)
        {
            return _descriptors.TryGetValue(path, out ProbeDescriptor? d) ? d : null;
        }
    }

    /// <summary>Returns the cached value for <paramref name="path"/>, or <c>null</c> if unread.</summary>
    public string? GetCached(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (_gate)
        {
            return _cache.TryGetValue(path, out string? value) ? value : null;
        }
    }

    /// <summary>
    /// Drops every cached value. Call after each Eval / Tick / Reset so the next
    /// <see cref="ReadAsync"/> goes back to the worker. We deliberately do NOT
    /// auto-refresh every previously-read probe — the caller decides which
    /// probes are visible enough to be worth re-reading.
    /// </summary>
    public void InvalidateAll()
    {
        lock (_gate)
        {
            _cache.Clear();
            _memoryCache.Clear();
        }
    }

    /// <summary>Returns the cached memory snapshot for <paramref name="path"/>, or <c>null</c> if unread.</summary>
    public MemorySnapshot? GetCachedMemory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (_gate)
        {
            return _memoryCache.TryGetValue(path, out MemorySnapshot? snap) ? snap : null;
        }
    }

    /// <summary>
    /// Reads a range of cells from a memory probe, updates the cache, and raises
    /// <see cref="MemoryUpdated"/> when any cell value differs from the prior
    /// cached snapshot. Returns null when no worker is attached or the path is
    /// unknown to the worker's memory_table.
    /// </summary>
    public async Task<MemorySnapshot?> ReadMemoryAsync(string path, ulong startAddress, int count, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || count <= 0) return null;
        (SimulationWorkerClient? worker, long generation) = CaptureWorker();
        if (worker is null) return null;

        MemoryReadResult result;
        try
        {
            result = await worker.ReadMemoryAsync(path, startAddress, count, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Unknown memory path or out-of-range — swallow and let caller fall back.
            return null;
        }

        MemorySnapshot snapshot = new(result.Path, result.StartAddress, result.CellWidth, result.Cells);
        bool changed;
        lock (_gate)
        {
            if (!IsCurrentWorker(worker, generation)) return null;
            changed = !_memoryCache.TryGetValue(path, out MemorySnapshot? prior)
                || !prior.Equals(snapshot);
            _memoryCache[path] = snapshot;
        }
        if (changed && IsCurrentWorkerSnapshot(worker, generation))
        {
            MemoryUpdated?.Invoke(this, new MemorySnapshotUpdatedEventArgs(snapshot));
        }
        return snapshot;
    }

    /// <summary>
    /// Reads <paramref name="path"/> from the worker, updates the cache, and
    /// raises <see cref="ValueUpdated"/> when the value changed. Returns the
    /// fresh value, or <c>null</c> if no worker is attached or the worker
    /// returned an error (unknown path, etc.). Errors are swallowed so the UI
    /// can keep rendering — a probe that no longer exists in the table just
    /// stays at its last-known value (or "-" if never read).
    /// </summary>
    public async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        (SimulationWorkerClient? worker, long generation) = CaptureWorker();
        if (worker is null) return null;

        SignalReadResult result;
        try
        {
            result = await worker.ReadSignalAsync(path, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Worker returned an ErrorResponse (e.g. "unknown probe path"). This
            // is expected for paths the enumerator filtered out (wide signals,
            // memories, Verilator tmps) — leave the cache untouched.
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        bool changed;
        lock (_gate)
        {
            if (!IsCurrentWorker(worker, generation)) return null;
            changed = !_cache.TryGetValue(path, out string? prior) || !string.Equals(prior, result.Value, StringComparison.Ordinal);
            _cache[path] = result.Value;
        }

        if (changed && IsCurrentWorkerSnapshot(worker, generation))
        {
            ValueUpdated?.Invoke(this, new ProbeValueUpdatedEventArgs(path, result.Value));
        }
        return result.Value;
    }

    private (SimulationWorkerClient? Worker, long Generation) CaptureWorker()
    {
        lock (_gate)
        {
            return (_worker, _workerGeneration);
        }
    }

    private bool IsCurrentWorkerSnapshot(SimulationWorkerClient worker, long generation)
    {
        lock (_gate)
        {
            return IsCurrentWorker(worker, generation);
        }
    }

    private bool IsCurrentWorker(SimulationWorkerClient worker, long generation) =>
        ReferenceEquals(_worker, worker) && _workerGeneration == generation;
}

public sealed class ProbeValueUpdatedEventArgs(string path, string value) : EventArgs
{
    public string Path { get; } = path;
    public string Value { get; } = value;
}

public sealed class ProbeValuesUpdatedEventArgs(IReadOnlyList<SignalReadOutcome> values) : EventArgs
{
    public IReadOnlyList<SignalReadOutcome> Values { get; } = values;
}

/// <summary>
/// Immutable record of a range read from a memory probe. Equality compares
/// every cell so cache change detection is correct.
/// </summary>
public sealed record MemorySnapshot(string Path, ulong StartAddress, int CellWidth, IReadOnlyList<string> Cells)
{
    public bool Equals(MemorySnapshot? other)
    {
        if (other is null) return false;
        if (StartAddress != other.StartAddress) return false;
        if (CellWidth != other.CellWidth) return false;
        if (!string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)) return false;
        if (Cells.Count != other.Cells.Count) return false;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (!string.Equals(Cells[i], other.Cells[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    public override int GetHashCode() => HashCode.Combine(Path, StartAddress, CellWidth, Cells.Count);
}

public sealed class MemorySnapshotUpdatedEventArgs(MemorySnapshot snapshot) : EventArgs
{
    public MemorySnapshot Snapshot { get; } = snapshot;
    public string Path => Snapshot.Path;
}
