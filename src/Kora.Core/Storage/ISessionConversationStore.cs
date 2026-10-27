using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

namespace Kora.Core.Storage;

/// <summary>One private consolidated authority; no caller-supplied append, replay or provider choice.</summary>
public interface ISessionConversationStore
{
    ValueTask<HostQuestionRecord> CreateVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
        ModelContextEnvelope context, ProviderProfileTuple profile, ModelProviderSelection destination,
        SessionProviderProfile providerState, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("The conversation store does not support verified local handoff review.");
    ValueTask<ConversationHandoffCommit> CommitVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
        HostQuestionRecord review, ModelContextEnvelope context, ModelContextEnvelope successor,
        ProviderProfileTuple profile, ModelProviderSelection destination, SessionProviderProfile providerState,
        Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("The conversation store does not support atomic verified local handoff.");
    ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
        HostRevision generation, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token);
    ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
        ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
        HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
        ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token);
    ValueTask<ConversationSubmissionReceipt> BeginConversationExecutionAsync(HostRequest request,
        ModelTurnProvenance provenance, long metadataRevision, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("The conversation store does not support admitted execution.");
    ValueTask<ConversationSubmissionReceipt> CompleteConversationExecutionAsync(HostRequest request,
        ModelTurnResult result, Func<bool> admitted, CancellationToken token) =>
        throw new InvalidOperationException("The conversation store does not support admitted assistant completion.");
    ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
        ConversationCursor? cursor, int limit, CancellationToken token);
    /// <summary>Reads one actually retained original record from the exact session, without lifecycle or activity changes.</summary>
    ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
        HostId<RequestIdentity> request, CancellationToken token);
}
