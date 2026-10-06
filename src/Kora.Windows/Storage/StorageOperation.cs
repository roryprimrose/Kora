using System.Diagnostics;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Storage;

internal sealed class StorageOperation : IDisposable
{
    private static readonly ActivitySource Source = new("Kora.Windows",
        typeof(StorageOperation).Assembly.GetName().Version?.ToString() ?? "1.0.0.0");
    private readonly Activity? activity;
    private readonly HostActivity? hostActivity;
    private readonly CancellationToken cancellationToken;
    private bool completed;

    internal StorageOperation(string name, CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;
        if (HostActivity.Current?.Activity is not null)
        {
            hostActivity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            activity = hostActivity.Activity;
            activity?.SetTag("kora.storage.boundary", name);
        }
        else
        {
            activity = Source.StartActivity(name);
        }
    }

    internal void Complete()
    {
        completed = true;
    }

    public void Dispose()
    {
        if (hostActivity is not null)
        {
            hostActivity.Complete(completed ? HostOperationOutcome.Completed
                : cancellationToken.IsCancellationRequested ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            hostActivity.Dispose();
            return;
        }
        activity?.SetStatus(completed ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        activity?.Dispose();
    }
}
