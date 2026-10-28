using Kora.Application.Hosting.ReviewedContext;
using Avalonia.Threading;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private MixedContextWorkflow? mixedWorkflow;
    private MixedContextReview? mixedReview;
    private bool mixedOpen;
    private long mixedControl;
    private long mixedSelection;
    private long mixedRevision;
    private string mixedBody = string.Empty;
    private string mixedOutcome = string.Empty;
    private HistoryContextCandidate[] mixedHistoryChoices = [];
    private MemorySummary[] mixedMemoryChoices = [];
    private ArtifactDefinition[] mixedInstructionChoices = [];
    private ConversationHistorySelection[] mixedHistory = [];
    private MemorySummary[] mixedMemories = [];
    private ArtifactDefinition? mixedInstruction;

    private bool MixedCurrent => mixedOpen && !closed && access.CanInspect && access.CanControl
        && mixedControl == access.ControlRevision && mixedSelection == selectionEpoch;
    internal bool MixedContextOpen => mixedOpen;
    internal void ReportMixedContextFailure(Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        conversationStatus = "Mixed context unavailable: " + exception.Message + " No successful operation or retry inferred.";
        NotifyConversation();
    }
    public bool CanOpenMixedContext => CanCompose && memories is not null && instructionUse is not null;
    public bool CanEditMixedContext => MixedCurrent && CanCompose;
    public bool CanReviewMixedContext => CanEditMixedContext && mixedHistory.Length != 0
        && mixedMemories.Length != 0 && mixedInstruction is not null && !string.IsNullOrWhiteSpace(mixedBody);
    public bool CanConfirmMixedContext => CanEditMixedContext && mixedReview?.CanConfirm == true;
    public IReadOnlyList<HistoryContextCandidate> MixedHistoryChoices => MixedCurrent ? mixedHistoryChoices : [];
    public IReadOnlyList<MemorySummary> MixedMemoryChoices => MixedCurrent ? mixedMemoryChoices : [];
    public IReadOnlyList<ArtifactDefinition> MixedInstructionChoices => MixedCurrent ? mixedInstructionChoices : [];
    public string MixedReviewText => MixedCurrent ? mixedReview?.Text ?? string.Empty : string.Empty;
    public string MixedOutcome => MixedCurrent ? mixedOutcome : string.Empty;
    public string MixedBody
    {
        get => MixedCurrent ? mixedBody : string.Empty;
        set
        {
            if (!CanEditMixedContext || string.Equals(value, mixedBody, StringComparison.Ordinal)) { return; }
            RetireMixedReview();
            mixedBody = value;
            NotifyMixedContext();
        }
    }
    public ArtifactDefinition? MixedInstruction
    {
        get => MixedCurrent ? mixedInstruction : null;
        set
        {
            if (!CanEditMixedContext || ReferenceEquals(value, mixedInstruction)
                || value is not null && !mixedInstructionChoices.Contains(value)) { return; }
            RetireMixedReview();
            mixedInstruction = value;
            NotifyMixedContext();
        }
    }

    internal void OpenMixedContext()
    {
        if (!CanOpenMixedContext)
        { throw new InvalidOperationException("Select one Active private session with available source brokers."); }
        ClearMixedContext();
        mixedOpen = true;
        mixedControl = access.ControlRevision;
        mixedSelection = selectionEpoch;
        mixedBody = composerBody;
        mixedHistoryChoices = ConversationMessages.Where(message => message.Outcome is not null)
            .SelectMany(message => message.AssistantResponse is null
                ? new[] { new HistoryContextCandidate(new(message, ConversationHistoryRole.User)) }
                : new[] { new HistoryContextCandidate(new(message, ConversationHistoryRole.User)),
                    new HistoryContextCandidate(new(message, ConversationHistoryRole.Assistant)) }).ToArray();
        mixedMemoryChoices = MemoryRecords.ToArray();
        mixedInstructionChoices = instructionUse!.Choices.ToArray();
        mixedWorkflow = conversation!.CreateMixedContextWorkflow(memories!.CreateContextWorkflow(), instructionUse);
        NotifyMixedContext();
    }

    internal void SelectMixedHistory(IReadOnlyList<HistoryContextCandidate> selection)
    {
        if (!CanEditMixedContext) { return; }
        if (selection.Any(item => !mixedHistoryChoices.Contains(item)))
        { throw new InvalidOperationException("Select only complete messages from the exact observed session page."); }
        RetireMixedReview();
        mixedHistory = selection.Select(item => item.Selection).ToArray();
        NotifyMixedContext();
    }

    internal void SelectMixedMemories(IReadOnlyList<MemorySummary> selection)
    {
        if (!CanEditMixedContext) { return; }
        if (selection.Any(item => !mixedMemoryChoices.Contains(item)))
        { throw new InvalidOperationException("Select only observed metadata from the original session memory list."); }
        RetireMixedReview();
        mixedMemories = selection.ToArray();
        NotifyMixedContext();
    }

    public Task ReviewMixedContextAsync() => RunConversationAsync(async (target, live) =>
    {
        RetireMixedReview();
        var version = mixedRevision;
        bool Original() => live() && MixedCurrent && mixedRevision == version;
        mixedReview = await (mixedWorkflow ?? throw new InvalidOperationException("Open a fresh mixed-context selection."))
            .ReviewAsync(target, mixedBody, mixedHistory,
                mixedInstruction ?? throw new InvalidOperationException("Select one enabled owned instruction."),
                mixedMemories, Original, lifetime.Token);
        mixedReview.Retired += () => Dispatcher.UIThread.Post(NotifyMixedContext);
        conversationStatus = "Inspect ALL complete sources, unchanged request and total budget. "
            + "Separately confirm ONE local-only tool-free answer or cancel. Nothing submitted yet.";
    });

    public Task ConfirmMixedContextAsync() => RunConversationAsync(async (_, live) =>
    {
        var review = mixedReview ?? throw new InvalidOperationException("Review the complete mixed context first.");
        var body = mixedBody;
        try
        {
            var receipt = await (mixedWorkflow ?? throw new InvalidOperationException("The source workflow was closed."))
                .ConfirmAsync(review, live, lifetime.Token);
            if (!live() || !MixedCurrent) { throw new OperationCanceledException("The original private selection changed."); }
            completedConversationMessage = receipt.Message;
            conversationPage = null;
            if (string.Equals(composerBody, body, StringComparison.Ordinal))
            { composerBody = string.Empty; Interlocked.Increment(ref composerBufferRevision); }
            RememberUnsent();
            mixedBody = string.Empty;
            mixedOutcome = receipt.Message.AssistantResponse is { } answer
                ? "ASSISTANT\n" + answer.Body
                : "HOST " + receipt.Message.Outcome + "/" + receipt.Message.Reason + "\nNo assistant answer was produced.";
            conversationStatus = "Mixed-context request retained. Genuine ASSISTANT answer or explicit HOST failure only. "
                + "No source-body copy, hosted transfer, tool, grant, memory write, fallback or automatic retry.";
        }
        finally { RetireMixedReview(clearOutcome: false); }
    }, submission: true);

    public void CancelMixedContext()
    {
        RetireMixedReview();
        conversationStatus = "Mixed context revoked; no late answer is admitted. Existing request/terminal receipts are not rolled back. "
            + "Provider ownership remains held until actual resource quiescence.";
        NotifyMixedContext();
        NotifyConversation();
    }

    internal void ClearMixedContext()
    {
        mixedOpen = false;
        RetireMixedReview(clearSelection: true);
        mixedWorkflow?.Dispose();
        mixedWorkflow = null;
        mixedBody = string.Empty;
        mixedHistoryChoices = [];
        mixedMemoryChoices = [];
        mixedInstructionChoices = [];
    }

    private void RetireMixedReview(bool clearOutcome = true, bool clearSelection = false)
    {
        mixedRevision++;
        mixedWorkflow?.Retire();
        mixedReview?.Dispose();
        mixedReview = null;
        if (clearSelection)
        {
            mixedHistory = [];
            mixedMemories = [];
            mixedInstruction = null;
        }
        if (clearOutcome) { mixedOutcome = string.Empty; }
    }

    private void NotifyMixedContext()
    {
        if (mixedOpen && !MixedCurrent) { ClearMixedContext(); }
        foreach (var name in new[] { nameof(CanOpenMixedContext), nameof(CanEditMixedContext),
            nameof(CanReviewMixedContext), nameof(CanConfirmMixedContext), nameof(MixedHistoryChoices),
            nameof(MixedMemoryChoices), nameof(MixedInstructionChoices), nameof(MixedInstruction),
            nameof(MixedBody), nameof(MixedReviewText), nameof(MixedOutcome) })
        { OnPropertyChanged(name); }
    }
}
