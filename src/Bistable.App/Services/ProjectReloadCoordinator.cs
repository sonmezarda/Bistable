namespace Bistable.App.Services;

/// <summary>
/// Runs at most one reload at a time. New save events cancel the active
/// elaboration and coalesce into the next pass.
/// </summary>
public sealed class ProjectReloadCoordinator(
    Func<IReadOnlyCollection<string>, CancellationToken, Task> reload) : IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _activeCancellation;
    private Task? _runner;
    private bool _disposed;

    public void Queue(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (string path in paths) _pending.Add(path);
            _activeCancellation?.Cancel();
            _runner ??= Task.Run(RunAsync);
        }
    }

    internal Task WhenIdleAsync()
    {
        lock (_gate) return _runner ?? Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        while (true)
        {
            string[] paths;
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_pending.Count == 0 || _disposed)
                {
                    _runner = null;
                    return;
                }
                paths = _pending.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                _pending.Clear();
                cancellation = new CancellationTokenSource();
                _activeCancellation = cancellation;
            }

            try
            {
                await reload(paths, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeCancellation, cancellation))
                    {
                        _activeCancellation = null;
                    }
                }
                cancellation.Dispose();
            }
        }
    }

    public void Dispose()
    {
        Task? runner;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _activeCancellation?.Cancel();
            runner = _runner;
        }
        _ = runner;
    }
}
