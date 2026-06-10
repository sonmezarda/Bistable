using System.Windows.Input;

namespace Bistable.App.Infrastructure;

public sealed class AsyncCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _isRunning;
    private CancellationTokenSource? _executionCancellation;

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter) => !_isRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        _executionCancellation = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        try
        {
            await execute(_executionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected command outcome. The owning
            // ViewModel publishes operation-specific status text.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // async void must not throw; callers observe the result through the ViewModel's Status property
        }
        finally
        {
            _executionCancellation.Dispose();
            _executionCancellation = null;
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
