using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private sealed partial class Fixture
    {
        internal bool TrackPendingRecords { get; set; }
        internal System.Collections.Concurrent.ConcurrentDictionary<HostId<RequestIdentity>, ConversationSubmissionReceipt> PendingRecords { get; } = [];
        private void RememberPendingRecord()
        {
            if (TrackPendingRecords) { PendingRecords[Retained!.Request.RequestId] = new(Retained, Task!.Task); }
        }
        private void SelectPendingRecord(HostRequest request)
        {
            if (!TrackPendingRecords) { return; }
            var receipt = PendingRecords[request.RequestId];
            Retained = receipt.Message;
            Task = new(receipt.Task, receipt.Message.Generation, "host", true, null);
        }
        public ValueTask<ConversationSubmissionReceipt> RetainPendingConversationMessageAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, IReadOnlyList<HostRequest> predecessors,
            Func<bool> admitted, CancellationToken token) =>
            RetainConversationMessageAsync(request, generation, expectedMetadataRevision, body, admitted, token);

        public async ValueTask<ConversationSubmissionReceipt> RetainOrdinaryPendingConversationAsync(HostRequest request,
            HostRevision generation, long expectedMetadataRevision, string body, IReadOnlyList<HostRequest> predecessors,
            ExpiredOrdinaryRequestSource? original, Func<bool> admitted, CancellationToken token)
        {
            var retained = await RetainPendingConversationMessageAsync(request, generation,
                expectedMetadataRevision, body, predecessors, admitted, token);
            Retained = retained.Message with { OrdinaryText = true, OriginalTextSource = original?.Message.Request };
            RememberPendingRecord();
            return new(Retained, retained.Task);
        }
    }
}
