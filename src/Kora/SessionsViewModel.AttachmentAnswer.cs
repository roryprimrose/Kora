using Kora.Application.Hosting;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public Task OpenAttachmentAnswerAsync() => RunConversationAsync(async (target, live) =>
    {
        if (!target.Authority.IsActive || !access.CanControl || conversationHeld || conversationSourceHeld)
        { throw new InvalidOperationException("Select a current Active private conversation without unconfirmed submission holds."); }
        await (attachments ?? throw new InvalidOperationException("The retained attachment viewer is unavailable."))
            .OpenAnswer(target, conversation!, SubmitAttachmentAnswer, live, lifetime.Token);
        conversationStatus = "Inspect this session's retained source, then review exact evidence and separately consent to one local tool-free answer. Nothing is automatically sent.";
    });

    private async Task<ConversationSubmissionReceipt> SubmitAttachmentAnswer(SessionAttachmentAnswerReview review,
        Func<bool> live, CancellationToken token)
    {
        if (!CanCompose || selected != review.Target || !live())
        { throw new InvalidOperationException("The exact original conversation is unavailable or held."); }
        conversationBusy = true;
        busy = true;
        Notify();
        try
        {
            var receipt = await conversation!.SubmitAttachmentAnswerAsync(review, live, token);
            if (!live()) { throw new OperationCanceledException("The private attachment answer presentation was revoked."); }
            PublishCompletedConversationMessage(receipt.Message);
            conversationStatus = receipt.Message.AssistantResponse is null
                ? "Attachment question retained with explicit HOST failure/unavailable status. No answer or retry claimed."
                : "Grounded ASSISTANT response retained in this conversation with host-resolved source references. Verify cited spans.";
            return receipt;
        }
        catch (ConversationSubmissionUnconfirmedException)
        {
            conversationHeld = true;
            conversationStatus = "Attachment answer completion is unconfirmed. Inspect exact conversation/task receipts; do not repeat the turn.";
            throw;
        }
        finally
        {
            conversationBusy = false;
            busy = false;
            Notify();
        }
    }
}
