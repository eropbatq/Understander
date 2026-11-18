using System.Windows.Input;

namespace Understander.ViewModels;

#pragma warning disable CS0067 // Event never used (CanExecuteChanged is always true, so it's never raised)
public class AsyncCommand : ICommand
{
    readonly Func<Task> _execute;
    public AsyncCommand(Func<Task> execute) { _execute = execute; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { _ = _execute(); }
    public event EventHandler? CanExecuteChanged;
}

public class AsyncCommand<T> : ICommand
{
    readonly Func<T, Task> _execute;
    public AsyncCommand(Func<T, Task> execute) { _execute = execute; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        if (parameter is T t) _ = _execute(t);
    }
    public event EventHandler? CanExecuteChanged;
}
#pragma warning restore CS0067
