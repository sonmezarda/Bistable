using Bistable.App.Services;
using Bistable.App.Services.Routing.Elk;

namespace Bistable.Tests.Routing;

public sealed class SchematicLayoutServiceTests
{
    [Fact]
    public async Task LayoutAsync_ReturnsGraph_FromUnderlyingRunner()
    {
        ElkGraph output = Graph("output");
        FakeElkRunner runner = new();
        runner.Enqueue(_ => output);
        await using SchematicLayoutService service = new(runner);

        ElkGraph result = await service.LayoutAsync(Graph("input"));

        Assert.Same(output, result);
        Assert.Equal(1, runner.LayoutCalls);
    }

    [Fact]
    public async Task LayoutAsync_Concurrent_SerialisesCalls()
    {
        using ManualResetEventSlim firstEntered = new(false);
        using ManualResetEventSlim releaseFirst = new(false);
        using ManualResetEventSlim secondEntered = new(false);
        FakeElkRunner runner = new();
        runner.Enqueue(_ =>
        {
            firstEntered.Set();
            releaseFirst.Wait(TimeSpan.FromSeconds(2));
            return Graph("first");
        });
        runner.Enqueue(_ =>
        {
            secondEntered.Set();
            return Graph("second");
        });
        await using SchematicLayoutService service = new(runner);

        Task<ElkGraph> first = service.LayoutAsync(Graph("input-1"));
        Assert.True(firstEntered.Wait(TimeSpan.FromMilliseconds(500)));

        Task<ElkGraph> second = service.LayoutAsync(Graph("input-2"));
        Assert.False(secondEntered.Wait(TimeSpan.FromMilliseconds(100)));

        releaseFirst.Set();
        Assert.Equal("first", (await first).Id);
        Assert.Equal("second", (await second).Id);
        Assert.Equal(2, runner.LayoutCalls);
    }

    [Fact]
    public async Task LayoutAsync_Cancel_ThrowsOperationCanceled_AndRestartsRunner()
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        using CancellationTokenSource cts = new();
        FakeElkRunner runner = new() { OnRestart = release.Set };
        runner.Enqueue(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(2));
            return Graph("cancelled");
        });
        await using SchematicLayoutService service = new(runner)
        {
            HardTimeout = TimeSpan.FromSeconds(5)
        };

        Task<ElkGraph> layout = service.LayoutAsync(Graph("input"), cts.Token);
        Assert.True(entered.Wait(TimeSpan.FromMilliseconds(500)));

        cts.Cancel();

        await AssertThrowsWithinAsync<OperationCanceledException>(layout, TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, runner.RestartCalls);
    }

    [Fact]
    public async Task LayoutAsync_AfterCancel_StillWorks()
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        using CancellationTokenSource cts = new();
        FakeElkRunner runner = new() { OnRestart = release.Set };
        runner.Enqueue(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(2));
            return Graph("cancelled");
        });
        runner.Enqueue(_ => Graph("after-cancel"));
        await using SchematicLayoutService service = new(runner)
        {
            HardTimeout = TimeSpan.FromSeconds(5)
        };

        Task<ElkGraph> cancelled = service.LayoutAsync(Graph("input"), cts.Token);
        Assert.True(entered.Wait(TimeSpan.FromMilliseconds(500)));
        cts.Cancel();
        await AssertThrowsWithinAsync<OperationCanceledException>(cancelled, TimeSpan.FromMilliseconds(500));

        ElkGraph result = await service.LayoutAsync(Graph("next"));

        Assert.Equal("after-cancel", result.Id);
        Assert.Equal(2, runner.LayoutCalls);
    }

    [Fact]
    public async Task LayoutAsync_HardTimeoutElapsed_ThrowsRoutingException()
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        FakeElkRunner runner = new() { OnRestart = release.Set };
        runner.Enqueue(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(2));
            return Graph("late");
        });
        await using SchematicLayoutService service = new(runner)
        {
            HardTimeout = TimeSpan.FromMilliseconds(100)
        };

        Task<ElkGraph> layout = service.LayoutAsync(Graph("input"));
        Assert.True(entered.Wait(TimeSpan.FromMilliseconds(500)));

        SchematicRoutingException ex =
            await AssertThrowsWithinAsync<SchematicRoutingException>(layout, TimeSpan.FromMilliseconds(750));

        Assert.Equal("Layout exceeded hard timeout (10 min). Process restarted.", ex.Message);
        Assert.Equal(1, runner.RestartCalls);
    }

    [Fact]
    public async Task SoftWarning_FiresOnce_AfterThreshold()
    {
        using ManualResetEventSlim release = new(false);
        using ManualResetEventSlim warningFired = new(false);
        FakeElkRunner runner = new();
        runner.Enqueue(_ =>
        {
            release.Wait(TimeSpan.FromSeconds(2));
            return Graph("slow");
        });
        await using SchematicLayoutService service = new(runner)
        {
            SoftWarningThreshold = TimeSpan.FromMilliseconds(50),
            HardTimeout = TimeSpan.FromSeconds(5)
        };
        int warnings = 0;
        service.LayoutStillRunning += (_, _) =>
        {
            Interlocked.Increment(ref warnings);
            warningFired.Set();
        };

        Task<ElkGraph> layout = service.LayoutAsync(Graph("input"));

        Assert.True(warningFired.Wait(TimeSpan.FromMilliseconds(500)));
        release.Set();
        Assert.Equal("slow", (await layout).Id);
        Assert.Equal(1, Volatile.Read(ref warnings));
    }

    [Fact]
    public async Task SoftWarning_DoesNotFire_WhenLayoutCompletesQuickly()
    {
        FakeElkRunner runner = new();
        runner.Enqueue(_ => Graph("quick"));
        await using SchematicLayoutService service = new(runner)
        {
            SoftWarningThreshold = TimeSpan.FromMilliseconds(100)
        };
        int warnings = 0;
        service.LayoutStillRunning += (_, _) => Interlocked.Increment(ref warnings);

        ElkGraph result = await service.LayoutAsync(Graph("input"));
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.Equal("quick", result.Id);
        Assert.Equal(0, Volatile.Read(ref warnings));
    }

    [Fact]
    public async Task DisposeAsync_CancelsInFlight_AndDisposesRunner()
    {
        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim release = new(false);
        FakeElkRunner runner = new() { OnDispose = release.Set };
        runner.Enqueue(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(2));
            return Graph("disposed");
        });
        SchematicLayoutService service = new(runner)
        {
            HardTimeout = TimeSpan.FromSeconds(5)
        };

        Task<ElkGraph> layout = service.LayoutAsync(Graph("input"));
        Assert.True(entered.Wait(TimeSpan.FromMilliseconds(500)));

        await service.DisposeAsync();

        Assert.True(runner.Disposed);
        await AssertThrowsWithinAsync<OperationCanceledException>(layout, TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task LayoutAsync_AfterDispose_ThrowsObjectDisposed()
    {
        SchematicLayoutService service = new(new FakeElkRunner());
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.LayoutAsync(Graph("input")));
    }

    private static ElkGraph Graph(string id) => new() { Id = id };

    private static async Task<TException> AssertThrowsWithinAsync<TException>(Task task, TimeSpan timeout)
        where TException : Exception
    {
        Task<TException> exceptionTask = Assert.ThrowsAnyAsync<TException>(async () => await task);
        Task completed = await Task.WhenAny(exceptionTask, Task.Delay(timeout));
        Assert.Same(exceptionTask, completed);
        return await exceptionTask;
    }

    private sealed class FakeElkRunner : IElkRunner
    {
        private readonly Queue<Func<ElkGraph, ElkGraph>> _responses = new();
        private readonly Lock _lock = new();
        private int _layoutCalls;
        private int _restartCalls;
        private bool _disposed;

        public Action? OnRestart { get; init; }

        public Action? OnDispose { get; init; }

        public int LayoutCalls => Volatile.Read(ref _layoutCalls);

        public int RestartCalls => Volatile.Read(ref _restartCalls);

        public bool Disposed
        {
            get
            {
                lock (_lock)
                {
                    return _disposed;
                }
            }
        }

        public void Enqueue(Func<ElkGraph, ElkGraph> response)
        {
            lock (_lock)
            {
                _responses.Enqueue(response);
            }
        }

        public ElkGraph Layout(ElkGraph input)
        {
            Interlocked.Increment(ref _layoutCalls);
            Func<ElkGraph, ElkGraph>? response;
            lock (_lock)
            {
                response = _responses.Count == 0 ? null : _responses.Dequeue();
            }

            return response?.Invoke(input) ?? input;
        }

        public void Restart()
        {
            Interlocked.Increment(ref _restartCalls);
            OnRestart?.Invoke();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            OnDispose?.Invoke();
        }
    }
}
