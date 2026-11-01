using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationExecutionStoreTests
{
    [Fact]
    public async Task LegacyStoreCannotPretendToSupportDispatchOrAssistantCompletion()
    {
        ISessionConversationStore store = new LegacyStore();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var token = TestContext.Current.CancellationToken;
        var begin = () => store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, token).AsTask();
        var finish = () => store.CompleteConversationExecutionAsync(request,
            new(ModelTurnOutcome.Succeeded, ModelTurnReason.None, provenance, new("not a retained answer", null)),
            static () => true, token).AsTask();
        await begin.Should().ThrowAsync<InvalidOperationException>();
        await finish.Should().ThrowAsync<InvalidOperationException>();
        var predecessor = new HostRequest(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var pending = () => store.RetainPendingConversationMessageAsync(request, new(1), 0, "original",
            [predecessor], () => true, token).AsTask();
        await pending.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not support pending original requests*");
        var immediate = () => store.RetainPendingConversationMessageAsync(request, new(1), 0, "original",
            [], () => true, token).AsTask();
        await immediate.Should().ThrowAsync<NotSupportedException>();
        var ordinary = () => store.RetainOrdinaryPendingConversationAsync(request, new(1), 0, "original",
            [], null, () => true, token).AsTask();
        await ordinary.Should().ThrowAsync<NotSupportedException>();
        var source = new ConversationMessage(predecessor, new(1), 1, 2, DateTimeOffset.UnixEpoch,
            "original", ModelTurnOutcome.Denied, ModelTurnReason.ContextExpired) { OrdinaryText = true };
        var attributed = () => store.RetainOrdinaryPendingConversationAsync(request, new(1), 0, "original",
            [], new(source, new(predecessor, new(2), HostTaskState.Denied)), () => true, token).AsTask();
        await attributed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot attest*");
    }

    private sealed class LegacyStore : ISessionConversationStore
    {
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => throw new NotSupportedException();
    }
}
