using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed record ConversationTurnCompletion(ConversationSubmissionReceipt? Receipt, Exception? Failure)
{
    public ConversationSubmissionReceipt RequireReceipt()
    {
        if (Failure is { } failure) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
        return Receipt ?? throw new InvalidOperationException("The submitted turn has no confirmed terminal receipt.");
    }
}
