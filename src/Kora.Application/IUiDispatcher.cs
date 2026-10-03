namespace Kora.Application;

public interface IUiDispatcher
{
    Task InvokeAsync(Func<Task> action);

    void Post(Action action);
}