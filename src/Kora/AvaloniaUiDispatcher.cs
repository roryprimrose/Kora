using Avalonia.Threading;

using Kora.Application;
using Kora.Core.Diagnostics;

namespace Kora;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Func<Task> action)
    {
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var activity = begin();
            try
            {
                await action();
                activity.Complete(HostOperationOutcome.Completed);
            }
            catch (OperationCanceledException)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        });
    }

    public void Post(Action action)
    {
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        Dispatcher.UIThread.Post(() =>
        {
            using var activity = begin();
            try
            {
                action();
                activity.Complete(HostOperationOutcome.Completed);
            }
            catch
            {
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        });
    }
}