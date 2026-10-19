using System.Text;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Memory;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private MemoryBatchWorkflow? memoryBatchWorkflow;
    private MemoryBatchReview? memoryBatchReview;
    private MemorySummary[] memoryBatchSelection = [];
    private string memoryBatchDetail = string.Empty;
    private long memoryBatchEpoch;
    private CancellationTokenSource? memoryBatchCancellation;
    private MemoryBatchOperation memoryBatchOperation;
    private long? memoryBatchCancelledEpoch;
    private bool memoryBatchNotificationRetired;

    public IReadOnlyList<MemoryBatchOperation> MemoryBatchOperations { get; } =
        [MemoryBatchOperation.Disable, MemoryBatchOperation.Forget];
    public MemoryBatchOperation MemoryBatchOperation
    {
        get => memoryBatchOperation;
        set
        {
            if (value == memoryBatchOperation) { return; }
            if (notifying) { RetireReentrantMemoryBatch(); return; }
            if (!CanReadMemories) { return; }
            RetireMemorySelection();
            memoryBatchOperation = value;
            NotifyMemories();
        }
    }
    public string MemoryBatchReviewLabel => $"Review selected {memoryBatchOperation.ToString().ToLowerInvariant()} subset";
    public string MemoryBatchConfirmLabel => $"Confirm sequential {memoryBatchOperation.ToString().ToUpperInvariant()}";

    public IReadOnlyList<MemorySummary> MemoryBatchSelection => MemorySourceCurrent ? memoryBatchSelection : [];
    public string MemoryBatchDetail
    {
        get
        {
            if (!MemorySourceCurrent) { RetireMemoryBatch(); }
            return memoryBatchDetail;
        }
    }
    public bool CanReviewMemoryBatch => CanReadMemories && MemorySourceCurrent && memoryBatchSelection.Length > 0;
    public bool CanConfirmMemoryBatch => CanReadMemories && MemorySourceCurrent && memoryBatchReview is not null;
    public bool CanCancelMemoryBatch => MemorySourceCurrent
        && (memoryBatchReview is not null || memoryBatchCancellation is not null || memoryBatchSelection.Length > 0);

    public void SelectMemoryBatch(IReadOnlyList<MemorySummary> selection)
    {
        if (notifying) { RetireReentrantMemoryBatch(); return; }
        if (!CanReadMemories || !MemorySourceCurrent) { return; }
        RetireMemorySelection();
        if (selection.Count > MemoryPolicy.MaximumEntries || selection.Any(row => !MemoryRecords.Contains(row)))
        {
            status = "Memory batch selection is stale. List and explicitly select again.";
        }
        else { memoryBatchSelection = selection.ToArray(); }
        NotifyMemories();
    }

    public Task ReviewMemoryBatchAsync()
    {
        if (notifying) { RetireReentrantMemoryBatch(); return Task.CompletedTask; }
        return RunAsync(ReviewMemoryBatchCoreAsync);
    }

    private async Task ReviewMemoryBatchCoreAsync()
    {
        var authority = RequireSelected().Authority;
        var control = access.ControlRevision;
        var epoch = memoryBatchEpoch;
        var selection = memoryBatchSelection.ToArray();
        var operation = memoryBatchOperation;
        memoryBatchReview?.Retire();
        memoryBatchReview = null;
        memoryBatchDetail = string.Empty;
        bool Eligible() => !closed && MemorySourceCurrent && selected?.Authority == authority
            && memoryBatchEpoch == epoch && access.ControlRevision == control;
        var workflow = memoryBatchWorkflow ??= new(memories
            ?? throw new InvalidOperationException("Memory management is unavailable."));
        var review = await workflow.ReviewAsync(operation, authority.SessionId, selection, Eligible, lifetime.Token);
        if (!Eligible()) { throw new OperationCanceledException("The selected memory subset changed."); }
        var text = new StringBuilder()
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"Review {review.Records.Length} exact admitted session memories for {operation.ToString().ToUpperInvariant()} only.")
            .AppendLine(MemoryBatchPolicy.Disclosure(operation));
        foreach (var record in review.Records)
        {
            text.AppendLine(Encoding.UTF8.GetString(MemoryCommandResult.Serialize(new("observed",
                "Exact content and classification; inspection alone does not accept, admit, disable, forget or export.")
                { Inspected = record })));
        }
        memoryBatchReview = review;
        memoryBatchDetail = text.ToString();
        status = $"Review every displayed exact record and classification, then separately confirm {operation.ToString().ToLowerInvariant()} or cancel. "
            + MemoryBatchPolicy.Disclosure(operation);
    }

    public Task ConfirmMemoryBatchAsync()
    {
        if (notifying) { RetireReentrantMemoryBatch(); return Task.CompletedTask; }
        return RunAsync(ConfirmMemoryBatchCoreAsync);
    }

    private async Task ConfirmMemoryBatchCoreAsync()
    {
        var authority = RequireSelected().Authority;
        var control = access.ControlRevision;
        var epoch = memoryBatchEpoch;
        var review = memoryBatchReview ?? throw new InvalidOperationException("Review the exact selected subset first.");
        var workflow = memoryBatchWorkflow ?? throw new InvalidOperationException("The memory review is unavailable.");
        memoryBatchReview = null;
        memoryBatchDetail = string.Empty;
        inspectedMemory = null;
        memoryDetail = string.Empty;
        memoryDraft = string.Empty;
        memoryMutationReady = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        memoryBatchCancellation = cancellation;
        NotifyMemoryBatch();
        try
        {
            var outcome = await workflow.ConfirmAsync(review, () => !closed && MemorySourceCurrent
                && selected?.Authority == authority && access.ControlRevision == control
                && memoryBatchEpoch == epoch, cancellation.Token);
            if (closed || !MemorySourceCurrent || selected?.Authority != authority || access.ControlRevision != control
                || memoryBatchEpoch != epoch && memoryBatchCancelledEpoch != epoch)
            {
                return;
            }
            RetireMemoryBatch();
            foreach (var item in outcome.Items)
            {
                if (item.State == MemoryBatchState.Succeeded && item.ActionResult?.Record is { } record)
                {
                    var updated = new MemorySummary(record.Id.Value, record.Revision.Value, record.Scope,
                        record.Review, record.Retention, record.CreatedAt);
                    memoryRows = [.. memoryRows.Select(row => row.Id == item.Id ? updated : row)];
                }
            }
            ApplyMemoryFilters();
            memoryBatchDetail = $"{review.Operation} results for {outcome.Items.Length} selected memories.\n"
                + (outcome.StoppedBy is { } stopped ? $"Sequence stopped: {stopped}.\n" : string.Empty)
                + MemoryBatchPolicy.Disclosure(review.Operation) + "\n"
                + string.Join("\n", outcome.Items.Select(item =>
                    $"{item.Id:D} reviewed revision {item.ReviewedRevision}: {item.State}"
                    + (item.ActionResult is { } result ? $" ({result.Outcome}/{result.Reason})" : string.Empty)));
            status = memoryBatchDetail;
        }
        finally
        {
            memoryBatchCancellation = null;
            NotifyMemoryBatch();
        }
    }

    public void CancelMemoryBatch()
    {
        if (notifying) { RetireReentrantMemoryBatch(); return; }
        if (closed) { return; }
        var epoch = memoryBatchCancelledEpoch ?? memoryBatchEpoch;
        memoryBatchCancellation?.Cancel();
        RetireMemoryBatch();
        memoryBatchCancelledEpoch = epoch;
        status = "Selected memory review retired. In-flight committed outcomes are not rolled back; "
            + "wait for the content-free per-record outcome before inspecting durable state manually.";
        NotifyMemories();
        OnPropertyChanged(nameof(Status));
    }

    private void RetireMemoryBatch()
    {
        memoryBatchEpoch++;
        memoryBatchCancelledEpoch = null;
        if (memoryBatchReview is not null || memoryBatchCancellation is not null) { memories?.ClearInspection(); }
        memoryBatchReview?.Retire();
        memoryBatchWorkflow?.Retire();
        if (closed) { memoryBatchWorkflow?.Dispose(); }
        memoryBatchReview = null;
        memoryBatchSelection = [];
        memoryBatchDetail = string.Empty;
    }

    private void RetireReentrantMemoryBatch()
    {
        memoryBatchNotificationRetired |= memoryBatchReview is not null || memoryBatchSelection.Length > 0
            || memoryBatchDetail.Length > 0;
        RetireMemoryBatch();
    }

    private void NotifyMemoryBatch()
    {
        var previous = notifying;
        notifying = true;
        try
        {
            do
            {
                // A callback can retire authority, never confirm it. Refresh bindings that saw the earlier body.
                memoryBatchNotificationRetired = false;
                OnPropertyChanged(nameof(MemoryBatchSelection));
                OnPropertyChanged(nameof(MemoryBatchOperation));
                OnPropertyChanged(nameof(MemoryBatchReviewLabel));
                OnPropertyChanged(nameof(MemoryBatchConfirmLabel));
                OnPropertyChanged(nameof(MemoryBatchDetail));
                OnPropertyChanged(nameof(CanReviewMemoryBatch));
                OnPropertyChanged(nameof(CanConfirmMemoryBatch));
                OnPropertyChanged(nameof(CanCancelMemoryBatch));
            }
            while (memoryBatchNotificationRetired);
        }
        finally { notifying = previous; }
    }
}
