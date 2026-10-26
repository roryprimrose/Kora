using Kora.Application.Memory.ModelProposals;
using Kora.Core.Conversation;
using Kora.Core.Memory;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ModelMemoryProposalReview? modelMemoryReview;
    public bool CanReviewModelMemoryProposal => CanReadConversation && access.CanControl
        && selected?.Authority.IsActive == true && !conversationSourceHeld && memories is not null;
    public bool CanAcceptModelMemoryProposal => CanReviewModelMemoryProposal && modelMemoryReview?.CanAccept == true;
    public bool CanAdmitModelMemoryProposal => CanReviewModelMemoryProposal && modelMemoryReview?.CanAdmit == true;
    public string ModelMemoryProposalDescription => modelMemoryReview?.Description ?? string.Empty;

    public Task ReviewModelMemoryProposalAsync(ConversationMessage message) => RunConversationAsync(async (target, live) =>
    {
        CancelModelMemoryProposal();
        if (memories is null || !access.CanControl || !target.Authority.IsActive || conversationSourceHeld
            || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
        { throw new InvalidOperationException("Select the exact retained ASSISTANT answer in this Active session."); }
        var page = conversationPage;
        var completed = completedConversationMessage;
        bool Current() => live() && access.CanControl && !conversationSourceHeld
            && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage);
        var review = await memories.ReviewModelProposalAsync(target, message, Current, lifetime.Token);
        if (!Current()) { review.Dispose(); throw new OperationCanceledException("Model-memory source revoked."); }
        modelMemoryReview = review;
        conversationStatus = "Inspect the complete USER/ASSISTANT lineage, value and classification below. "
            + "Accept and then Admit are separate; nothing has been saved or used.";
    });

    public Task AcceptModelMemoryProposalAsync() => ActOnModelMemoryProposalAsync(admit: false, accept: true);
    public Task RejectModelMemoryProposalAsync() => ActOnModelMemoryProposalAsync(admit: false, accept: false);
    public Task AdmitModelMemoryProposalAsync() => ActOnModelMemoryProposalAsync(admit: true, accept: false);

    private Task ActOnModelMemoryProposalAsync(bool admit, bool accept) => RunConversationAsync(async (_, _) =>
    {
        var review = modelMemoryReview ?? throw new InvalidOperationException("Review the exact model-memory proposal first.");
        var result = await memories!.ActOnModelProposalAsync(review, admit, accept, lifetime.Token);
        conversationStatus = result.Outcome != MemoryOutcome.Succeeded
            ? "Model-memory operation not confirmed: " + result.Outcome + " / " + result.Reason
                + ". Inspect session memories and audit receipts before a fresh review; no retry or rollback."
            : admit ? "Exact SESSION memory admitted by original-user confirmation. No memory use, model turn, grant or disclosure."
            : accept ? "Exact candidate accepted for this SESSION; still pending. Separately Admit, or Cancel without saving."
            : "Model-memory proposal rejected. No memory body saved or used.";
        if (admit || !accept || result.Outcome != MemoryOutcome.Succeeded) { CancelModelMemoryProposal(); }
    });

    public void CancelModelMemoryProposal()
    {
        modelMemoryReview?.Dispose();
        modelMemoryReview = null;
        NotifyModelMemoryProposal();
    }

    private void NotifyModelMemoryProposal()
    {
        if (modelMemoryReview is { Current: false })
        {
            modelMemoryReview.Dispose();
            modelMemoryReview = null;
        }
        OnPropertyChanged(nameof(CanReviewModelMemoryProposal));
        OnPropertyChanged(nameof(CanAcceptModelMemoryProposal));
        OnPropertyChanged(nameof(CanAdmitModelMemoryProposal));
        OnPropertyChanged(nameof(ModelMemoryProposalDescription));
    }
}
