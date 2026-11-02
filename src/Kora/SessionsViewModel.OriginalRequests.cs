using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal void PresentOriginalRequestSubmission(ConversationSubmissionReceipt receipt)
    {
        if (!CanCompose || selected?.Authority.SessionId != receipt.Message.Request.SessionId
            || selected.Authority.Generation != receipt.Message.Generation) { return; }
        if (!PublishCompletedConversationMessage(receipt.Message)) { return; }
        conversationStatus = "Original request: " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + (receipt.Message.AssistantResponse is null ? ". No assistant answer was retained." : ". Genuine ASSISTANT answer retained.")
            + " Existing unsent input is unchanged.";
        NotifyConversation();
    }
}
