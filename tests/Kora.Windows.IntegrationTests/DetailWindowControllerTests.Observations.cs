using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("session")]
    public async Task ObservationReaderFormatsInertStructurePreservesExactSourceAndRetiresAllDelayedControls(string retirement)
    {
        using var f = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var item = RetainedLocalObservation.Capture(ReadOnlyCapabilityCatalog.Version, "{}",
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "original observation")), DateTimeOffset.UnixEpoch);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "answer"), Observations = new(1, [item]),
        };
        var content = new AdmittedDetailContent(new(new(item.Id), 3), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedLocalObservation, DetailSensitivity.DisclosureConfirmationRequired,
            "Observed version", "Original local observation, not current truth", item.ResultJson, conversationMessage: message);
        var live = true;
        var source = true;
        Task<AdmittedDetailContent> Resolve(CancellationToken token) =>
            source ? Task.FromResult(content) : throw new InvalidOperationException("The exact source is unavailable.");
        await f.Controller.OpenConversationDetailAsync(content, null, () => live, Resolve, CancellationToken.None);
        var view = f.Views.Single();
        var generation = view.State.Generation;
        view.State.ActiveText.Should().Contain("\n").And.Contain("original observation");
        view.State.Content!.Source.Should().Be(item.ResultJson);
        view.State.SetSource(true);
        view.State.ActiveText.Should().Be(item.ResultJson);
        view.State.CanInteractWithFinalDocument.Should().BeFalse();
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
        if (retirement is "private") { live = false; }
        if (retirement is "source") { source = false; }
        if (retirement is "session") { f.Controller.RevokeSession(request.SessionId); }
        await f.Controller.RevalidateConversationDetailsAsync();
        view.State.Content.Should().BeNull();
        view.State.ActiveText.Should().BeEmpty();
        view.State.CompleteRender(generation, "late observation", "Rendered", false).Should().BeFalse();
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
    }
}
