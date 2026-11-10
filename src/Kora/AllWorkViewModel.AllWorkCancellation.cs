using Kora.Application.Hosting;

using AllWorkStopOutcome = Kora.Application.Hosting.AllWorkStopResult.OutcomeKind;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private Func<Func<bool>, CancellationToken, Task<AllWorkStopResult>>? stopAll;
    private bool stopAllBusy;
    private bool presentationOpen = true;
    private long presentationRevision;
    private string stopAllStatus = "Stop All is separate from this bounded page. Opening, filtering, selection, Find and Refresh never stop work.";

    public bool CanStopAll => !closed && presentationOpen && !stopAllBusy && access.CanControl && stopAll is not null;
    public string StopAllStatus => stopAllStatus;
    internal Task StopAllOperation { get; private set; } = Task.CompletedTask;
    internal AllWorkStopResult? StopAllResult { get; private set; }

    internal void BindStopAll(Func<Func<bool>, CancellationToken, Task<AllWorkStopResult>> stop)
    { stopAll = stop; NotifyStopAll(); }

    internal void SetPresentationOpen(bool open)
    {
        if (closed || presentationOpen == open) { return; }
        presentationOpen = open;
        if (!open)
        {
            presentationRevision++;
            Invalidate();
            StopAllResult = null;
            status = "Work overview presentation retired. Private records and continuation were cleared; explicitly Refresh.";
            stopAllStatus = "Original Stop All presentation retired. Reopening does not renew its review or confirmation.";
        }
        Notify();
    }

    public Task StopAllAsync()
    {
        if (!CanStopAll) { return Task.CompletedTask; }
        return StopAllOperation = RunStopAllAsync();
    }

    private async Task RunStopAllAsync()
    {
        stopAllBusy = true;
        var revision = access.ControlRevision;
        var nativeRevision = presentationRevision;
        var stop = stopAll!;
        stopAllStatus = "Reviewing or stopping the complete host set, independent of this page. Awaiting separate original-channel confirmation or actual resource joining. Kora stays open.";
        NotifyStopAll();
        try
        {
            var result = await stop(() => !closed && presentationOpen && presentationRevision == nativeRevision && access.CanControl
                && access.ControlRevision == revision && ReferenceEquals(stopAll, stop), lifetime.Token);
            if (!closed && presentationOpen && presentationRevision == nativeRevision)
            {
                StopAllResult = result;
                stopAllStatus = result.Summary;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            if (!closed && presentationOpen && presentationRevision == nativeRevision)
            { stopAllStatus = new AllWorkStopResult(AllWorkStopOutcome.PartialUnconfirmed).Summary; }
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
