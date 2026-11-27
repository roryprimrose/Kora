using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionConversationStorePermissionTests
{
    [Fact]
    public async Task LegacyStoreFailsClosedForPermissionBoundRetentionWithoutCallingLegacyRetain()
    {
        var store = new LegacyStore();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        var policy = new SessionPermissionPolicy(request.SessionId, new(1), new(1), SessionPermissionLevel.ApproveAll);
        var approval = new AutomaticApprovalAudit(request, policy, "conversation.voice-readback",
            HostOperationEffect.BoundedWrite, "request-readback", new string('a', 64));
        var operation = () => ((ISessionConversationStore)store).RetainPermissionBoundConversationAsync(
            request, new(1), 0, "original", [], policy, approval, static () => true, TestContext.Current.CancellationToken).AsTask();
        await operation.Should().ThrowAsync<InvalidOperationException>();
        store.LegacyRetains.Should().Be(0);
    }

    private sealed class LegacyStore : ISessionConversationStore
    {
        internal int LegacyRetains { get; private set; }
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token)
        { LegacyRetains++; throw new NotSupportedException(); }
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => throw new NotSupportedException();
    }
}
