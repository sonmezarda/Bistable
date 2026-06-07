using Bistable.App.Services;

namespace Bistable.App.Services.Routing.Elk;

public interface IElkRunner : IDisposable
{
    ElkGraph Layout(ElkGraph input);

    void Restart();
}

/// <summary>
/// Per-window owner for ELK layout requests. The underlying ELK process is a
/// single-threaded worker, so this service serialises requests, moves the
/// blocking layout call off the UI thread, and translates cancellation into a
/// process restart.
/// </summary>
public sealed class SchematicLayoutService : IAsyncDisposable
{
    private static readonly TimeSpan DefaultHardTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultSoftWarningThreshold = TimeSpan.FromSeconds(10);

    private readonly IElkRunner _runner;
    private readonly SemaphoreSlim _layoutGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public SchematicLayoutService(IElkRunner? runner = null)
    {
        _runner = runner ?? new ElkRunner();
    }

    public event EventHandler? LayoutStillRunning;

    public TimeSpan HardTimeout { get; init; } = DefaultHardTimeout;

    public TimeSpan SoftWarningThreshold { get; init; } = DefaultSoftWarningThreshold;

    public async Task<ElkGraph> LayoutAsync(ElkGraph input, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using CancellationTokenSource acquisitionCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        await _layoutGate.WaitAsync(acquisitionCts.Token).ConfigureAwait(false);

        CancellationTokenSource? hardTimeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationTokenSource? softWarningCts = null;
        Task? softWarningTask = null;
        Task<ElkGraph>? layoutTask = null;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            hardTimeoutCts = new CancellationTokenSource(HardTimeout);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disposeCts.Token, hardTimeoutCts.Token);

            layoutTask = Task.Run(() => _runner.Layout(input), CancellationToken.None);
            softWarningCts = new CancellationTokenSource();
            softWarningTask = RaiseSoftWarningAsync(layoutTask, softWarningCts.Token);

            Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
            Task completed = await Task.WhenAny(layoutTask, cancellationTask).ConfigureAwait(false);
            if (completed == layoutTask)
            {
                if (linkedCts.IsCancellationRequested)
                {
                    await HandleCancellationAfterLayoutCompletionAsync(
                        layoutTask,
                        hardTimeoutCts,
                        cancellationToken).ConfigureAwait(false);
                }

                return await layoutTask.ConfigureAwait(false);
            }

            ObserveFault(layoutTask);

            if (_disposeCts.IsCancellationRequested)
            {
                throw new OperationCanceledException(_disposeCts.Token);
            }

            RestartRunner();

            if (hardTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new SchematicRoutingException("Layout exceeded hard timeout (10 min). Process restarted.");
            }

            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (softWarningCts is not null)
            {
                await CancelSoftWarningAsync(softWarningCts, softWarningTask).ConfigureAwait(false);
                softWarningCts.Dispose();
            }

            linkedCts?.Dispose();
            hardTimeoutCts?.Dispose();
            _layoutGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _runner.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RaiseSoftWarningAsync(Task layoutTask, CancellationToken cancellationToken)
    {
        try
        {
            Task delayTask = Task.Delay(SoftWarningThreshold, cancellationToken);
            Task completed = await Task.WhenAny(layoutTask, delayTask).ConfigureAwait(false);
            if (completed == delayTask && !cancellationToken.IsCancellationRequested && !layoutTask.IsCompleted)
            {
                LayoutStillRunning?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal completion/cancellation path; no warning should be raised.
        }
    }

    private static async Task CancelSoftWarningAsync(CancellationTokenSource cts, Task? warningTask)
    {
        cts.Cancel();
        if (warningTask is null) return;

        try
        {
            await warningTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the layout completes before the soft threshold.
        }
    }

    private void RestartRunner()
    {
        try
        {
            _runner.Restart();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // DisposeAsync owns process teardown in this race.
        }
    }

    private async Task HandleCancellationAfterLayoutCompletionAsync(
        Task<ElkGraph> layoutTask,
        CancellationTokenSource hardTimeoutCts,
        CancellationToken cancellationToken)
    {
        if (!_disposeCts.IsCancellationRequested)
        {
            RestartRunner();
        }

        if (hardTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new SchematicRoutingException("Layout exceeded hard timeout (10 min). Process restarted.");
        }

        if (_disposeCts.IsCancellationRequested)
        {
            throw new OperationCanceledException(_disposeCts.Token);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await layoutTask.ConfigureAwait(false);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
