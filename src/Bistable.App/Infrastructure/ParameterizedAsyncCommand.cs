using System.Windows.Input;

namespace Bistable.App.Infrastructure;

/// <summary>
/// Async ICommand that accepts a typed parameter. Used for commands triggered
/// from list/tree views (the parameter being the row's identifier) where the
/// work is asynchronous, e.g. spinning up a sub-simulation worker.
/// </summary>
public sealed class ParameterizedAsyncCommand<T>(Func<T, CancellationToken, Task> execute, Predicate<T>? canExecute = null) : ICommand
{
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isRunning && parameter is T typed && (canExecute?.Invoke(typed) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter) || parameter is not T typed)
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute(typed, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // async void must not throw; callers observe the result through the ViewModel's Status property
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
