using Bistable.App.Infrastructure;

namespace Bistable.Tests.Infrastructure;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task Cancel_PropagatesCancellation_AndRestoresCanExecute()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AsyncCommand command = new(async cancellationToken =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        });

        command.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        command.Cancel();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => !command.IsRunning);

        Assert.True(command.CanExecute(null));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
