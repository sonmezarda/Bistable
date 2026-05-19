using System.Windows.Input;

namespace Bistable.App.Infrastructure;

public sealed class ParameterizedRelayCommand<T>(Action<T> execute, Predicate<T>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T typedParameter && (canExecute?.Invoke(typedParameter) ?? true);

    public void Execute(object? parameter)
    {
        if (parameter is T typedParameter && CanExecute(typedParameter))
        {
            execute(typedParameter);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
