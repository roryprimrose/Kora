using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private sealed partial class Fixture
    {
        internal bool TrackPendingRecords { get; set; }
        internal Action<HostRequest, Func<bool>>? BeforePendingTerminal { get; set; }
        private readonly Lock pendingRecordGate = new();
        internal System.Collections.Concurrent.ConcurrentDictionary<HostId<RequestIdentity>, ConversationSubmissionReceipt> PendingRecords { get; } = [];
        [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Retained), nameof(Task))]
        private void RememberPendingRecord(ConversationSubmissionReceipt receipt)
        {
            Retained = receipt.Message;
            Task = new(receipt.Task, receipt.Message.Generation, "host", true, null);
            if (TrackPendingRecords) { PendingRecords[receipt.Message.Request.RequestId] = receipt; }
        }
        private ConversationSubmissionReceipt ReadPendingRecord(HostRequest request) =>
            TrackPendingRecords ? PendingRecords[request.RequestId] : new(Retained!, Task!.Task);
        public ValueTask<ConversationSubmissionReceipt> RetainPendingConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, IReadOnlyList<HostRequest> predecessors,
            Func<bool> admitted, CancellationToken token) =>
            RetainConversationMessageAsync(request, generation, expectedMetadataRevision, body, admitted, token);
        public ValueTask<ConversationSubmissionReceipt> CancelPendingConversationSubmissionAsync(HostRequest request,
            SessionWorkspaceEntry expected, Func<bool> admitted, CancellationToken token)
        {
            if (expected != new SessionWorkspaceEntry(Session, Metadata) || !admitted())
            { throw new InvalidOperationException("The canonical pending cancellation source changed."); }
            return CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, admitted, token);
        }
    }
}
