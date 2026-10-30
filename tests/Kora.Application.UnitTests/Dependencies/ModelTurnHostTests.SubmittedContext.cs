using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task CanonicalHistoryHandoffMetadataBindsGenuineAnswerWithoutModelClaimsOrInspectionEffects(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        var source = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await service.ReviewHistoryAsync(new(f.Session, null), "question",
            [new(source, ConversationHistoryRole.Assistant), new(source, ConversationHistoryRole.User)], static () => true, Token, origin);
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new("I used private memory and a source that was never submitted", null));
        var receipt = await service.SubmitHistoryAsync(review, static () => true, Token);
        var metadata = receipt.Message.SubmittedContext!;
        metadata.Should().NotBeNull();
        metadata.Provenance.Should().Be(receipt.Message.Execution);
        metadata.Profile.Should().Be(Profile());
        metadata.Generation.Should().Be(receipt.Message.Generation);
        metadata.CanonicalUtf8Bytes.Should().Be(f.Adapter.Wire!.Length);
        metadata.Sources.Select(item => item.Role).Should().Equal(ConversationHistoryRole.User, ConversationHistoryRole.Assistant);
        metadata.Sources.Should().OnlyContain(item => item.Request == source.Request.RequestId && item.Revision == source.Revision);
        JsonSerializer.Serialize(metadata).Should().NotContain(source.Body).And.NotContain(source.AssistantResponse!.Body);
        using var wire = JsonDocument.Parse(f.Adapter.Wire);
        wire.RootElement.GetProperty("Evidence")[0].TryGetProperty("HistorySource", out _).Should().BeFalse();
        var reads = f.HistoryReads;
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var detail = await service.ReadDetailAsync(new(f.Session, null), receipt.Message, static () => true, Token, submittedContext: true);
        detail.Origin.Should().Be(DetailContentOrigin.RetainedAnswerContext);
        detail.Source.Should().Contain("1. Earlier USER").And.Contain("2. Earlier ASSISTANT")
            .And.NotContain("I used private memory");
        f.HistoryReads.Should().Be(reads, "inspection reads only source-row metadata, not source bodies");
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        var state = new Kora.Application.Presentation.DetailViewerState(detail, static () => true);
        state.TryGetCopySource(true, true, out _).Should().BeFalse();
        state.CanInteractWithFinalDocument.Should().BeFalse();
    }

    [Fact]
    public async Task UnconfirmedMetadataReceiptCannotBePublishedOrRetriedAsSuccess()
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        using var review = await service.ReviewHistoryAsync(new(f.Session, null), "question",
            [new(source, ConversationHistoryRole.User)], static () => true, Token);
        f.TerminalReceiptFault = "submitted-context";
        await service.Invoking(item => item.SubmitHistoryAsync(review, static () => true, Token))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
    }

    private sealed partial class Fixture
    {
        public ValueTask<ImmutableArray<SubmittedHistoryAvailability>> ReadSubmittedHistoryAvailabilityAsync(
            ConversationMessage answer, CancellationToken token) =>
            ValueTask.FromResult(answer.SubmittedContext!.Sources.Select(source =>
                HistoryMessages.TryGetValue(source.Request, out var message) && message.Revision == source.Revision
                    ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable).ToImmutableArray());
    }
}
