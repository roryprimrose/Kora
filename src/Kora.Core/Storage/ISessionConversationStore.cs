using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>One private consolidated authority; no caller-supplied append, replay or provider choice.</summary>
public interface ISessionConversationStore
{
    ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
        HostRevision generation, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
        ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
        HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
        ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
        ConversationCursor? cursor, int limit, CancellationToken token);
}
