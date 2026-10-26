using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Kora.Application.Memory;
using Kora.Core.Commands;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionMemoryContext? memoryContextWorkflow;
    private SessionMemoryContextReview? memoryContextReview;
    private MemorySummary[] memoryContextSelection = [];
    private string memoryContextRequest = string.Empty;
    private string memoryContextOutcome = string.Empty;

    public string MemoryContextOutcome => MemorySourceCurrent ? memoryContextOutcome : string.Empty;

    public IReadOnlyList<MemorySummary> MemoryContextSelection => MemorySourceCurrent ? memoryContextSelection : [];
    public string MemoryContextRequest
    {
        get => MemorySourceCurrent ? memoryContextRequest : string.Empty;
        set
        {
            if (notifying || !CanReadMemories || !MemorySourceCurrent || conversationBusy
                || string.Equals(value, memoryContextRequest, StringComparison.Ordinal)) { return; }
            RetireMemoryContext(clearSelection: false);
            memoryContextRequest = value;
            NotifyMemoryContext();
        }
    }
    public bool CanReviewMemoryContext => CanCompose && MemorySourceCurrent && memoryContextSelection.Length > 0
        && !string.IsNullOrWhiteSpace(memoryContextRequest);
    public bool CanConfirmMemoryContext => CanCompose && MemorySourceCurrent && memoryContextReview?.Current == true;
    public bool CanCancelMemoryContext => memoryContextReview is not null || memoryContextSelection.Length > 0;
    public string MemoryContextDetail
    {
        get
        {
            var review = memoryContextReview;
            if (!MemorySourceCurrent || review?.Current != true) { return string.Empty; }
            var text = new StringBuilder("Review complete values, classifications, exact Session or Device-profile scope and request below. "
                + "Confirm authorizes ONE local-only tool-free turn. No other memory/history/attachments, remembering or hosted transmission. "
                + "The original request and genuine final answer or HOST failure follow conversation retention; "
                + "selected source bodies are not copied into retained conversation history.\n");
            foreach (var record in review.Records)
            {
                text.AppendLine("Classification: " + record.Candidate!.ContentClass + "; scope: " + record.Scope.Kind);
                text.AppendLine(JsonSerializer.Serialize(new { record.Scope, record.Candidate }));
            }
            return text.AppendLine("Complete request:").Append(review.UserRequest).ToString();
        }
    }

    public void SelectMemoryContext(IReadOnlyList<MemorySummary> selection)
    {
        if (notifying || !CanReadMemories || !MemorySourceCurrent || conversationBusy) { return; }
        RetireMemoryContext();
        if (selection.Count > Kora.Core.Dependencies.ModelContextEnvelope.MaximumEvidenceItems
            || selection.Any(row => !MemoryRecords.Contains(row)))
        { status = "Select no more than sixteen exact visible memories; nothing was read."; }
        else { memoryContextSelection = selection.ToArray(); }
        NotifyMemoryContext();
    }

    public Task ReviewMemoryContextAsync() => RunConversationAsync(async (target, live) =>
    {
        if (!MemorySourceCurrent || memoryContextSelection.Any(row => !MemoryRecords.Contains(row)))
        { throw new InvalidOperationException("Refresh and explicitly select exact session memory metadata."); }
        var management = memories ?? throw new InvalidOperationException("Memory management is unavailable.");
        var workflow = memoryContextWorkflow ??= deviceProfileMemory
            ? management.CreateDeviceProfileContextWorkflow() : management.CreateContextWorkflow();
        memoryContextReview?.Dispose();
        memoryContextReview = null;
        var review = await workflow.ReviewAsync(target, memoryContextSelection, memoryContextRequest, live, lifetime.Token);
        memoryContextReview = review;
        review.Retired += () => Dispatcher.UIThread.Post(NotifyMemoryContext);
        conversationStatus = "Review complete selected memory content, classifications, scope and request. "
            + "Separately confirm one local-only tool-free turn, or cancel. Review expires after two minutes.";
    });

    public Task ConfirmMemoryContextAsync() => RunConversationAsync(async (_, live) =>
    {
        var review = memoryContextReview ?? throw new InvalidOperationException("Review the exact memory and request first.");
        var workflow = memoryContextWorkflow ?? throw new InvalidOperationException("Memory context is unavailable.");
        try
        {
            var receipt = await conversation!.SubmitMemoryContextAsync(workflow, review, live, lifetime.Token);
            if (!live()) { throw new OperationCanceledException("The original memory presentation changed."); }
            completedConversationMessage = receipt.Message;
            conversationPage = null;
            memoryContextRequest = string.Empty;
            memoryContextOutcome = receipt.Message.AssistantResponse is { } answer
                ? "ASSISTANT\n" + answer.Body
                : "HOST " + receipt.Message.Outcome + "/" + receipt.Message.Reason
                    + "\nNo assistant answer was produced. Unqualified providers remain unavailable.";
            conversationStatus = "Reviewed memory turn retained. ASSISTANT content is a genuine final answer only; "
                + "HOST " + receipt.Message.Outcome + "/" + receipt.Message.Reason
                + " is not an answer. No automatic remember/write, retry or hosted transmission.";
        }
        finally { RetireMemoryContext(clearOutcome: false); }
    }, submission: true);

    public void CancelMemoryContext()
    {
        RetireMemoryContext();
        conversationStatus = "Memory source review/use revoked. No late answer is admitted. "
            + "An already retained request or terminal outcome is not rolled back; inspect exact conversation receipts.";
        NotifyMemoryContext();
        NotifyConversation();
    }

    private void RetireMemoryContext(bool clearSelection = true, bool clearOutcome = true)
    {
        memoryContextWorkflow?.Retire();
        memoryContextReview?.Dispose();
        memoryContextReview = null;
        if (clearSelection) { memoryContextSelection = []; }
        if (clearOutcome) { memoryContextOutcome = string.Empty; }
    }

    private void ClearMemoryContext()
    {
        RetireMemoryContext();
        memoryContextWorkflow?.Dispose();
        memoryContextWorkflow = null;
        memoryContextRequest = string.Empty;
    }

    private void NotifyMemoryContext()
    {
        foreach (var name in new[] { nameof(MemoryContextSelection), nameof(MemoryContextRequest),
            nameof(MemoryContextDetail), nameof(MemoryContextOutcome), nameof(CanReviewMemoryContext),
            nameof(CanConfirmMemoryContext), nameof(CanCancelMemoryContext) })
        { OnPropertyChanged(name); }
    }
}
