using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed class ConversationQueuedSubmission
{
    internal ConversationQueuedSubmission(ConversationSubmissionReceipt receipt, int position,
        Task<ConversationTurnCompletion> completion)
    {
        Receipt = receipt;
        Position = position;
        Completion = completion;
    }

    public ConversationSubmissionReceipt Receipt { get; }
    public int Position { get; }
    public Task<ConversationTurnCompletion> Completion { get; }
}
