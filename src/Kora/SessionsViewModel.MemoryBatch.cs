using System.Text;
using Kora.Application.Memory;
using Kora.Core.Commands;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private MemoryBatchDisableWorkflow? memoryBatchWorkflow;
    private MemoryBatchDisableReview? memoryBatchReview;
    private IReadOnlyList<MemorySummary> memoryBatchSelection = [];
    private string memoryBatchDetail = string.Empty;
    private long memoryBatchEpoch;
    private CancellationTokenSource? memoryBatchCancellation;

    public IReadOnlyList<MemorySummary> MemoryBatchSelection => MemorySourceCurrent ? memoryBatchSelection : [];
    public string MemoryBatchDetail => MemorySourceCurrent ? memoryBatchDetail : string.Empty;
    public bool CanReviewMemoryBatch => CanReadMemories && MemorySourceCurrent && memoryBatchSelection.Count > 0;
    public bool CanConfirmMemoryBatch => CanReadMemories && MemorySourceCurrent && memoryBatchReview is not null;
    public bool CanCancelMemoryBatch => MemorySourceCurrent
        && (memoryBatchReview is not null || memoryBatchCancellation is not null || memoryBatchSelection.Count > 0);

    public void SelectMemoryBatch(IReadOnlyList<MemorySummary> selection)
    {
        if (notifying || !CanReadMemories || !MemorySourceCurrent) { return; }
        RetireMemorySelection();
        if (selection.Any(row => !MemoryRecords.Contains(row)))
        {
            status = "Memory batch selection is stale. List and explicitly select again.";
        }
        else { memoryBatchSelection = selection.ToArray(); }
        NotifyMemories();
    }

    public Task ReviewMemoryBatchAsync() => RunAsync(async () =>
    {
        var authority = RequireSelected().Authority;
        var control = access.ControlRevision;
        var epoch = memoryBatchEpoch;
        var selection = memoryBatchSelection.ToArray();
        memoryBatchReview = null;
        memoryBatchDetail = string.Empty;
        bool Eligible() => !closed && MemorySourceCurrent && selected?.Authority == authority
            && memoryBatchEpoch == epoch && access.ControlRevision == control;
        var workflow = memoryBatchWorkflow ??= new(memories
            ?? throw new InvalidOperationException("Memory management is unavailable."));
        var review = await workflow.ReviewAsync(authority.SessionId, selection, Eligible, lifetime.Token);
        if (!Eligible()) { throw new OperationCanceledException("The selected memory subset changed."); }
        var text = new StringBuilder()
            .AppendLine($"Review {review.Records.Length} exact enabled session memories for DISABLE only.")
            .AppendLine(MemoryBatchDisableOutcome.Disclosure);
        foreach (var record in review.Records)
        {
            text.AppendLine(Encoding.UTF8.GetString(MemoryCommandResult.Serialize(new("observed",
                "Exact content and classification; this review does not accept, admit, forget or export.")
                { Inspected = record })));
        }
        memoryBatchReview = review;
        memoryBatchDetail = text.ToString();
        status = "Review every displayed exact record and classification, then separately confirm disable or cancel. "
            + MemoryBatchDisableOutcome.Disclosure;
    });

    public Task ConfirmMemoryBatchAsync() => RunAsync(async () =>
    {
        var authority = RequireSelected().Authority;
        var control = access.ControlRevision;
        var epoch = memoryBatchEpoch;
        var review = memoryBatchReview ?? throw new InvalidOperationException("Review the exact selected subset first.");
        var workflow = memoryBatchWorkflow ?? throw new InvalidOperationException("The memory review is unavailable.");
        memoryBatchReview = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        memoryBatchCancellation = cancellation;
        NotifyMemoryBatch();
        try
        {
            var outcome = await workflow.ConfirmAsync(review, () => !closed && MemorySourceCurrent
                && selected?.Authority == authority && access.ControlRevision == control
                && memoryBatchEpoch == epoch, cancellation.Token);
            if (closed || !MemorySourceCurrent || selected?.Authority != authority || access.ControlRevision != control)
            {
                return;
            }
            RetireMemoryBatch();
            foreach (var item in outcome.Items)
            {
                if (item.State == MemoryBatchDisableState.Succeeded && item.ActionResult?.Record is { } record)
                {
                    var updated = new MemorySummary(record.Id.Value, record.Revision.Value, record.Scope,
                        record.Review, record.Retention, record.CreatedAt);
                    memoryRows = [.. memoryRows.Select(row => row.Id == item.Id ? updated : row)];
                }
            }
            ApplyMemoryFilters();
            memoryBatchDetail = $"Disable results for {outcome.Items.Length} selected memories.\n"
                + (outcome.StoppedBy is { } stopped ? $"Sequence stopped: {stopped}.\n" : string.Empty)
                + MemoryBatchDisableOutcome.Disclosure + "\n"
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
    });

    public void CancelMemoryBatch()
    {
        if (notifying || closed) { return; }
        memoryBatchCancellation?.Cancel();
        RetireMemoryBatch();
        status = "Selected memory disable review retired. In-flight committed outcomes are not rolled back; "
            + "wait for the content-free per-record outcome before inspecting durable state manually.";
        NotifyMemories();
        OnPropertyChanged(nameof(Status));
    }

    private void RetireMemoryBatch()
    {
        memoryBatchEpoch++;
        memoryBatchWorkflow?.Retire();
        if (closed) { memoryBatchWorkflow?.Dispose(); }
        memoryBatchReview = null;
        memoryBatchSelection = [];
        memoryBatchDetail = string.Empty;
    }

    private void NotifyMemoryBatch()
    {
        OnPropertyChanged(nameof(MemoryBatchSelection));
        OnPropertyChanged(nameof(MemoryBatchDetail));
        OnPropertyChanged(nameof(CanReviewMemoryBatch));
        OnPropertyChanged(nameof(CanConfirmMemoryBatch));
        OnPropertyChanged(nameof(CanCancelMemoryBatch));
    }
}
