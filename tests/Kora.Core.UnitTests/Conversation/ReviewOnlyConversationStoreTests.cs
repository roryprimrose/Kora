using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ReviewOnlyConversationStoreTests
{
    [Fact]
    public async Task LegacyStoreCannotSilentlyClaimVerifiedReviewOrCanonicalHandoffSupport()
    {
        ISessionConversationStore store = new ReviewOnlyStore();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.CreateVerifiedLocalHandoffAsync(null!, null!, null!, null!, null!, static () => true,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.CommitVerifiedLocalHandoffAsync(null!, null!, null!, null!, null!, null!, null!,
                static () => true, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.CancelPendingConversationSubmissionAsync(null!, null!, static () => true,
                TestContext.Current.CancellationToken));
    }

    private sealed class ReviewOnlyStore : ISessionConversationStore
    {
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session, HostId<RequestIdentity> request, CancellationToken token) => throw new NotSupportedException();
    }
}
