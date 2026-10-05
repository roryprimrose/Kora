using System.Windows.Input;

namespace Kora.Application.Infrastructure;

public sealed class AsyncCommand(
    Func<Task> execute,
    Action<Exception> handleException,
    Func<bool>? canExecute = null) : ICommand
{
    private bool isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            handleException(exception);
        }
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await execute();
        }
        finally
        {
            isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}