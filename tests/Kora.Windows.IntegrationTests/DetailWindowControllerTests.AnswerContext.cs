using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("session")]
    public async Task ContextInspectorUsesExactNativeSourceAndSuppressesPrivateStaleAndLatePublication(string retirement)
    {
        using var f = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var message = new ConversationMessage(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, "answer"),
        };
        var content = new AdmittedDetailContent(AnswerContextInspection.Reference(message), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedAnswerContext, DetailSensitivity.DisclosureConfirmationRequired,
            "Context submitted for this answer", "Host-recorded historical metadata", AnswerContextInspection.Describe(message),
            conversationMessage: message);
        var live = true;
        var source = true;
        Task<AdmittedDetailContent> Resolve(CancellationToken token) =>
            source ? Task.FromResult(content) : throw new InvalidOperationException("The exact answer is unavailable.");
        await f.Controller.OpenConversationDetailAsync(content, null, () => live, Resolve, CancellationToken.None);
        var view = f.Views.Single();
        view.State.ActiveText.Should().Contain("Unknown / not recorded");
        var generation = view.State.Generation;
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
        if (retirement is "private") { live = false; }
        if (retirement is "source") { source = false; }
        if (retirement is "session") { f.Controller.RevokeSession(request.SessionId); }
        await f.Controller.RevalidateConversationDetailsAsync();
        view.State.Content.Should().BeNull();
        view.State.ActiveText.Should().BeEmpty();
        view.State.CompleteRender(generation, "late metadata", "Rendered", false).Should().BeFalse();
    }
}
