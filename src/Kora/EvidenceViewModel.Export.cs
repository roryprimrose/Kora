using Avalonia.Controls;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private EvidencePageExportWindowController? export;
    private Action<Task>? trackExport;
    private Action<Task>? releaseExport;
    private CancellationTokenSource? exportCancellation;
    private Task exportOperation = Task.CompletedTask;

    internal bool CanExportPage => export is not null && CanQuery && page is not null
        && query.CanExport(page) && exportOperation.IsCompleted;
    internal void BindExport(EvidencePageExportWindowController controller, Action<Task> track, Action<Task> release)
    {
        export = controller;
        trackExport = track;
        releaseExport = release;
    }

    internal async Task ExportPageAsync(Window owner)
    {
        if (!CanExportPage)
        {
            Failure(logger, nameof(InvalidOperationException));
            if (!closed) { status = "The original evidence page export is unavailable. Search a permitted page and start a fresh review."; }
            Notify();
            return;
        }
        var original = page!;
        var exportSource = query.CaptureExportSource(original);
        var revision = filterRevision;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        exportCancellation = cancellation;
        exportOperation = RunExport();
        trackExport!(exportOperation);
        Notify();
        await exportOperation;
        releaseExport!(exportOperation);
        Notify();

        bool Live() => !closed && !busy && canInspect() && filterRevision == revision
            && ReferenceEquals(page, original) && !cancellation.IsCancellationRequested;

        async Task RunExport()
        {
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Storage);
            activity.Complete(HostOperationOutcome.Failed);
            try
            {
                var result = await export!.ShowAsync(owner, exportSource, Live, cancellation.Token);
                if (Live()) { status = result.Message; }
                activity.Complete(result.Outcome switch
                {
                    EvidencePageExportOutcome.Saved => HostOperationOutcome.Completed,
                    EvidencePageExportOutcome.Cancelled => HostOperationOutcome.Cancelled,
                    _ => HostOperationOutcome.Failed,
                });
            }
            catch (OperationCanceledException)
            {
                if (Live()) { status = "Evidence export cancelled. No automatic retry or rollback is claimed."; }
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure(logger, exception.GetType().Name);
                if (Live()) { status = "Evidence export unavailable or unconfirmed. No automatic retry or rollback is claimed; inspect any selected destination before a fresh review."; }
            }
            finally
            {
                if (ReferenceEquals(exportCancellation, cancellation)) { exportCancellation = null; }
                cancellation.Dispose();
                Notify();
            }
        }
    }

    private void RetireExport() => exportCancellation?.Cancel();
}
