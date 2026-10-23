using System.Globalization;

using Kora.Application.Hosting;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IReadOnlyList<ArtifactDefinition> instructionChoices = [];
    private ArtifactDefinition? instructionChoice;
    private ScopedInstructionUseBroker.InstructionContextReview? instructionReview;

    public IReadOnlyList<ArtifactDefinition> InstructionChoices => CanRead ? instructionChoices : [];
    public ArtifactDefinition? InstructionChoice
    {
        get => CanRead ? instructionChoice : null;
        set
        {
            if (!CanCompose || value is not null && !instructionChoices.Any(item => ReferenceEquals(item, value))) { return; }
            ClearInstructionReview();
            instructionChoice = value;
            NotifyInstructionContext();
        }
    }
    public bool CanRefreshInstructions => CanCompose && instructionUse is not null;
    public bool CanReviewInstruction => CanRefreshInstructions && instructionChoice is not null && CanSubmitConversation;
    public bool CanSubmitInstruction => CanSubmitConversation && instructionReview?.CanSubmit == true;
    public string InstructionReviewText => instructionReview is { } review && review.IsCurrent()
        ? "ONE local request in this exact conversation. No tools, hosted transfer, grants or future applicability. "
            + "Instruction is lower-trust guidance, not host policy. Submitting retains the original request and any genuine answer; "
            + "the instruction is pinned for this task, not installed as conversation policy.\n\n"
            + "COMPLETE PINNED INSTRUCTION\n" + review.InstructionText
            + "\n\nCOMPLETE ORIGINAL REQUEST\n" + review.Body
        : "No current instruction-use review. Refresh enabled owned instructions, select one and review the complete source and request. "
            + "Ordinary Submit remains unchanged and does not use this selection.";

    public Task RefreshInstructionsAsync() => RunConversationAsync((_, _) =>
    {
        ClearInstructionReview();
        instructionChoice = null;
        instructionChoices = (instructionUse ?? throw new InvalidOperationException("Instruction use is unavailable.")).Choices;
        conversationStatus = instructionChoices.Count.ToString(CultureInfo.InvariantCulture)
            + " enabled owned instruction candidates. No source read, activation or submission. Choose one, then review.";
        return Task.CompletedTask;
    });

    public Task ReviewInstructionAsync() => RunConversationAsync(async (target, live) =>
    {
        ClearInstructionReview();
        var choice = instructionChoice ?? throw new InvalidOperationException("Select an enabled owned instruction.");
        var buffer = Interlocked.Read(ref composerBufferRevision);
        var body = composerBody;
        bool Current() => live() && Interlocked.Read(ref composerBufferRevision) == buffer
            && string.Equals(composerBody, body, StringComparison.Ordinal) && ReferenceEquals(instructionChoice, choice);
        instructionReview = await (instructionUse ?? throw new InvalidOperationException("Instruction use is unavailable."))
            .ReviewAsync(target, choice, body, Current, lifetime.Token);
        conversationStatus = "Complete pinned instruction and original request ready for separate one-shot submission. "
            + "This review expires after two minutes. No model call or message retention yet.";
    });

    public Task SubmitInstructionAsync() => RunConversationAsync(async (target, live) =>
    {
        var review = instructionReview ?? throw new InvalidOperationException("Review the exact instruction and request first.");
        if (review.Target != target) { throw new InvalidOperationException("The original instruction review belongs to another session."); }
        try
        {
            var receipt = await conversation!.SubmitInstructionAsync(review, live, lifetime.Token);
            if (!live()) { throw new OperationCanceledException("The original private conversation changed."); }
            if (string.Equals(composerBody, review.Body, StringComparison.Ordinal)) { composerBody = string.Empty; }
            RememberUnsent();
            conversationPage = null;
            completedConversationMessage = receipt.Message;
            conversationStatus = "One-shot instruction request retained. HOST " + receipt.Message.Outcome + " / "
                + receipt.Message.Reason + (receipt.Message.AssistantResponse is null
                    ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately.")
                + " No instruction applies to the next request. No fallback or automatic retry.";
        }
        finally { ClearInstructionReview(); }
    }, submission: true);

    public void ClearInstructionReview()
    {
        instructionReview?.Retire();
        instructionReview = null;
        OnPropertyChanged(nameof(InstructionReviewText));
        OnPropertyChanged(nameof(CanSubmitInstruction));
    }

    private void ClearInstructionContext()
    {
        ClearInstructionReview();
        instructionChoice = null;
        instructionChoices = [];
    }

    private void NotifyInstructionContext()
    {
        if (closed || !access.CanInspect) { ClearInstructionContext(); }
        else if (instructionReview is { } review && !review.IsCurrent()) { ClearInstructionReview(); }
        foreach (var name in new[] { nameof(InstructionChoices), nameof(InstructionChoice), nameof(InstructionReviewText),
            nameof(CanRefreshInstructions), nameof(CanReviewInstruction), nameof(CanSubmitInstruction) })
        { OnPropertyChanged(name); }
    }
}
