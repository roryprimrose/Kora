using System.Windows.Input;

namespace Kora.Application.Infrastructure;

public sealed class AsyncCommand(
    Func<Task> execute,
    Action<Exception> handleException,
    Func<bool>? canExecute = null,
    Func<bool>? canExecuteWhileExecuting = null) : ICommand
{
    private int executionCount;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => (executionCount == 0 || canExecuteWhileExecuting?.Invoke() == true)
        && (canExecute?.Invoke() ?? true);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "ICommand.Execute requires void; failures are routed to the supplied exception handler.")]
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

        executionCount++;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await execute();
        }
        finally
        {
            executionCount--;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}