using Avalonia.Threading;

using Kora.Application;

namespace Kora;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Func<Task> action) =>
        await Dispatcher.UIThread.InvokeAsync(action);

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}