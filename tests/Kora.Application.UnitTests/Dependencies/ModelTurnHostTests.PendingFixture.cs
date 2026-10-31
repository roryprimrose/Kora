using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private sealed partial class Fixture
    {
        internal bool TrackPendingRecords { get; set; }
        internal System.Collections.Concurrent.ConcurrentDictionary<HostId<RequestIdentity>, ConversationSubmissionReceipt> PendingRecords { get; } = [];
        private void RememberPendingRecord(ConversationSubmissionReceipt receipt)
        {
            Retained = receipt.Message;
            Task = Task! with { Task = receipt.Task };
            if (TrackPendingRecords) { PendingRecords[receipt.Message.Request.RequestId] = receipt; }
        }
        private ConversationSubmissionReceipt ReadPendingRecord(HostRequest request) =>
            TrackPendingRecords ? PendingRecords[request.RequestId] : new(Retained!, Task!.Task);
        public ValueTask<ConversationSubmissionReceipt> RetainPendingConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, IReadOnlyList<HostRequest> predecessors,
            Func<bool> admitted, CancellationToken token) =>
            RetainConversationMessageAsync(request, generation, expectedMetadataRevision, body, admitted, token);
    }
}
