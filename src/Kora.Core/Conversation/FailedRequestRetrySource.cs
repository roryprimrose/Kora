using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Immutable failed content and terminal task evidence, never authority to replay either task.</summary>
public sealed record FailedRequestRetrySource(ConversationMessage Message, HostTaskRecord Task)
{
    public ModelProviderSelection Provider => Message.Execution!.Selection;

    public void Validate()
    {
        Message.Validate();
        if (Message.Execution is null)
        { throw new InvalidOperationException("Retry unavailable: this retained failure has no recorded original provider. No current default or historical provider will be inferred."); }
        if (Message.Outcome is null or ModelTurnOutcome.Succeeded or ModelTurnOutcome.Cancelled
            or ModelTurnOutcome.TimedOut or ModelTurnOutcome.Unknown
            || Task.Request != Message.Request || !Task.IsTerminal || Task.State != Message.ExpectedTaskState
            || Task.Revision.Value != Message.Revision)
        { throw new InvalidOperationException("Retry requires one exact confirmed terminal failure, not active, successful, cancelled or uncertain work."); }
    }
}
