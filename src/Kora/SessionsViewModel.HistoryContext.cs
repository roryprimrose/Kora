using Kora.Application.Hosting;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationHistoryReview? historyContextReview;
    private ConversationHistorySelection[] historyContextSelection = [];
    private HistoryContextCandidate[] historyContextCandidates = [];
    private long historyContextEpoch;
    private long historyContextControl;
    private long historyContextSelectionEpoch;
    private bool historyContextOpen;

    private bool HistoryContextCurrent => !closed && access.CanInspect && access.CanControl
        && access.ControlRevision == historyContextControl && selectionEpoch == historyContextSelectionEpoch;
    public IReadOnlyList<HistoryContextCandidate> HistoryContextCandidates =>
        HistoryContextCurrent && historyContextOpen ? historyContextCandidates : [];
    public string HistoryContextReviewText => historyContextReview?.Text ?? string.Empty;
    public bool CanOpenHistoryContext => CanCompose && ConversationMessages.Any(message => message.Outcome is not null);
    public bool CanSelectHistoryContext => CanCompose && historyContextOpen && HistoryContextCurrent;
    public bool CanReviewHistoryContext => CanSelectHistoryContext && ComposerDirty && historyContextSelection.Length != 0;
    public bool CanConfirmHistoryContext => CanSelectHistoryContext && historyContextReview?.IsCurrent == true;

    internal void OpenHistoryContext()
    {
        if (!CanOpenHistoryContext) { throw new InvalidOperationException("Read the exact conversation before selecting earlier messages."); }
        ClearHistoryContext();
        historyContextOpen = true;
        historyContextControl = access.ControlRevision;
        historyContextSelectionEpoch = selectionEpoch;
        historyContextCandidates = ConversationMessages.Where(message => message.Outcome is not null)
            .SelectMany(message => message.AssistantResponse is null
                ? new[] { new HistoryContextCandidate(new(message, ConversationHistoryRole.User)) }
                : new[] { new HistoryContextCandidate(new(message, ConversationHistoryRole.User)),
                    new HistoryContextCandidate(new(message, ConversationHistoryRole.Assistant)) }).ToArray();
        NotifyHistoryContext();
    }

    internal void SelectHistoryContext(IEnumerable<HistoryContextCandidate> candidates)
    {
        if (!CanSelectHistoryContext) { return; }
        var selectedCandidates = candidates.ToArray();
        if (selectedCandidates.Any(candidate => !historyContextCandidates.Contains(candidate)))
        { throw new InvalidOperationException("Select only whole messages from this exact observed conversation page."); }
        RetireHistoryContextReview();
        historyContextSelection = selectedCandidates.Select(candidate => candidate.Selection).ToArray();
        NotifyHistoryContext();
    }

    public Task ReviewHistoryContextAsync() => RunConversationAsync(async (target, live) =>
    {
        if (!historyContextOpen || historyContextSelection.Length == 0)
        { throw new InvalidOperationException("Select earlier whole USER or ASSISTANT messages explicitly."); }
        RetireHistoryContextReview();
        var epoch = historyContextEpoch;
        var buffer = composerBufferRevision;
        var body = composerBody;
        bool Original() => live() && historyContextOpen && historyContextEpoch == epoch && composerBufferRevision == buffer;
        var review = await conversation!.ReviewHistoryAsync(target, body, historyContextSelection, Original, lifetime.Token);
        if (!Original()) { review.Dispose(); throw new OperationCanceledException("Original history selection or new request changed."); }
        historyContextReview = review;
        conversationStatus = "Review every selected whole message and the new request below. Confirm separately for one local-only tool-free answer. Nothing submitted yet.";
    });

    public Task ConfirmHistoryContextAsync() => RunConversationAsync(async (_, live) =>
    {
        var review = historyContextReview ?? throw new InvalidOperationException("Review the exact selected history and request first.");
        var body = composerBody;
        try
        {
            var receipt = await conversation!.SubmitHistoryAsync(review, live, lifetime.Token);
            if (!live()) { throw new OperationCanceledException("Private conversation presentation changed."); }
            if (string.Equals(composerBody, body, StringComparison.Ordinal))
            { composerBody = string.Empty; Interlocked.Increment(ref composerBufferRevision); }
            RememberUnsent();
            conversationPage = null;
            completedConversationMessage = receipt.Message;
            conversationStatus = "Reviewed history request retained. HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                + (receipt.Message.AssistantResponse is null ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately.")
                + " No tools, hosted transfer, memory creation, fallback or automatic retry.";
        }
        finally { ClearHistoryContext(); }
    }, submission: true);

    internal void ClearHistoryContext()
    {
        RetireHistoryContextReview();
        historyContextOpen = false;
        historyContextSelection = [];
        historyContextCandidates = [];
        NotifyHistoryContext();
    }

    private void RetireHistoryContextReview()
    {
        historyContextEpoch++;
        historyContextReview?.Dispose();
        historyContextReview = null;
    }

    internal void RefreshHistoryContextReview()
    {
        if (historyContextOpen && !HistoryContextCurrent
            || historyContextReview is { IsCurrent: false }) { ClearHistoryContext(); }
        NotifyHistoryContext();
    }

    private void NotifyHistoryContext()
    {
        foreach (var name in new[] { nameof(HistoryContextCandidates), nameof(HistoryContextReviewText), nameof(CanOpenHistoryContext),
            nameof(CanSelectHistoryContext), nameof(CanReviewHistoryContext), nameof(CanConfirmHistoryContext) })
        { OnPropertyChanged(name); }
    }

    internal sealed record HistoryContextCandidate(ConversationHistorySelection Selection)
    {
        public string Role => Selection.Role == ConversationHistoryRole.User ? "USER" : "ASSISTANT";
        public long Sequence => Selection.Message.Sequence;
        public string Body => Selection.Role == ConversationHistoryRole.User
            ? Selection.Message.Body : Selection.Message.AssistantResponse!.Body;
    }
}
