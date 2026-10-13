using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>A possible durable request must be inspected by exact identity, never blindly resubmitted.</summary>
public sealed class ConversationSubmissionUnconfirmedException(HostRequest request, Exception cause)
    : IOException("Conversation submission is unconfirmed; inspect the exact retained request/task without retry or rollback claims.", cause)
{
    public HostRequest Request { get; } = request;
}
