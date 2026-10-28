using Kora.Application.Hosting;

using AllWorkStopOutcome = Kora.Application.Hosting.AllWorkStopResult.OutcomeKind;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private Func<Func<bool>, CancellationToken, Task<AllWorkStopResult>>? stopAll;
    private bool stopAllBusy;
    private string stopAllStatus = "Stop All is separate from this bounded page. Opening, filtering, selection, Find and Refresh never stop work.";

    public bool CanStopAll => !closed && !stopAllBusy && access.CanControl && stopAll is not null;
    public string StopAllStatus => stopAllStatus;
    internal Task StopAllOperation { get; private set; } = Task.CompletedTask;
    internal AllWorkStopResult? StopAllResult { get; private set; }

    internal void BindStopAll(Func<Func<bool>, CancellationToken, Task<AllWorkStopResult>> stop)
    { stopAll = stop; NotifyStopAll(); }

    public Task StopAllAsync()
    {
        if (!CanStopAll) { return Task.CompletedTask; }
        return StopAllOperation = RunStopAllAsync();
    }

    private async Task RunStopAllAsync()
    {
        stopAllBusy = true;
        var revision = access.ControlRevision;
        var stop = stopAll!;
        stopAllStatus = "Reviewing or stopping the complete host set, independent of this page. Awaiting separate original-channel confirmation or actual resource joining. Kora stays open.";
        NotifyStopAll();
        try
        {
            var result = await stop(() => !closed && access.CanControl
                && access.ControlRevision == revision && ReferenceEquals(stopAll, stop), lifetime.Token);
            if (!closed)
            {
                StopAllResult = result;
                stopAllStatus = result.Summary;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (!closed) { stopAllStatus = new AllWorkStopResult(AllWorkStopOutcome.PartialUnconfirmed).Summary; }
        }
        finally
        {
            stopAllBusy = false;
            NotifyStopAll();
            if (closed && !busy) { lifetime.Dispose(); }
        }
    }

    private void NotifyStopAll()
    {
        OnPropertyChanged(nameof(CanStopAll));
        OnPropertyChanged(nameof(StopAllStatus));
    }

    private void RetireStopAllPresentation()
    {
        stopAll = null;
        StopAllResult = null;
        stopAllStatus = string.Empty;
    }
}
