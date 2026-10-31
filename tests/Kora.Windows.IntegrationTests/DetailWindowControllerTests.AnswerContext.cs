using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;

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
        var message = RecordedContextAnswer();
        var content = RecordedContextContent(message, SubmittedHistoryAvailability.RetainedRevision);
        var live = true;
        var source = true;
        Task<AdmittedDetailContent> Resolve(CancellationToken token) =>
            source ? Task.FromResult(content) : throw new InvalidOperationException("The exact answer is unavailable.");
        await f.Controller.OpenConversationDetailAsync(content, null, () => live, Resolve, CancellationToken.None);
        var view = f.Views.Single();
        view.State.ActiveText.Should().Contain("exact source-row revision is retained").And.NotContain(message.Body)
            .And.NotContain(message.AssistantResponse!.Body).And.NotContain("earlier private source body marker");
        var generation = view.State.Generation;
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
        if (retirement is "private") { live = false; }
        if (retirement is "source") { source = false; }
        if (retirement is "session") { f.Controller.RevokeSession(message.Request.SessionId); }
        await f.Controller.RevalidateConversationDetailsAsync();
        view.State.Content.Should().BeNull();
        view.State.ActiveText.Should().BeEmpty();
        view.State.CompleteRender(generation, "late metadata", "Rendered", false).Should().BeFalse();
    }

    [Theory]
    [InlineData("publication", "availability-lost")]
    [InlineData("publication", "availability-restored")]
    [InlineData("publication", "origin")]
    [InlineData("publication", "private")]
    [InlineData("publication", "selection")]
    [InlineData("publication", "session")]
    [InlineData("publication", "unchanged")]
    [InlineData("revalidation", "availability-lost")]
    [InlineData("revalidation", "availability-restored")]
    [InlineData("revalidation", "origin")]
    [InlineData("revalidation", "private")]
    [InlineData("revalidation", "selection")]
    [InlineData("revalidation", "session")]
    [InlineData("revalidation", "unchanged")]
    public async Task RecordedContextDetailsFenceAwaitedFinalPublicationAndRevalidationWithoutOriginCoercion(string phase, string change)
    {
        using var f = new Fixture();
        var token = TestContext.Current.CancellationToken;
        var message = RecordedContextAnswer();
        message.SubmittedContext.Should().NotBeNull();
        var availability = change is "availability-restored"
            ? SubmittedHistoryAvailability.Unavailable : SubmittedHistoryAvailability.RetainedRevision;
        var content = RecordedContextContent(message, availability);
        var admitted = true;
        var assistantOrigin = false;
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AdmittedDetailContent> Resolve(CancellationToken cancellation)
        {
            if (++calls == (phase is "publication" ? 2 : 3))
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellation);
            }
            return assistantOrigin
                ? new(new(new(message.Execution!.Turn.Value), message.Revision), DetailContentKind.Markdown,
                    DetailContentOrigin.RetainedAssistant, DetailSensitivity.DisclosureConfirmationRequired,
                    "Retained complete ASSISTANT answer", "Private original answer", message.AssistantResponse!.Body,
                    conversationMessage: message)
                : RecordedContextContent(message, availability);
        }
        if (phase is "revalidation")
        {
            await f.Controller.OpenConversationDetailAsync(content, null, () => admitted, Resolve, token);
            f.Views.Single().Shown.Should().BeTrue();
            f.Views.Single().Activations.Should().Be(1);
        }
        Task operation = phase is "publication"
            ? f.Controller.OpenConversationDetailAsync(content, null, () => admitted, Resolve, token)
            : f.Controller.RevalidateConversationDetailsAsync();
        FakeView? prepared = null;
        long generation = 0;
        try
        {
            await entered.Task.WaitAsync(token);
            prepared = f.Views.Should().ContainSingle().Subject;
            generation = prepared.State.Generation;
            if (phase is "publication")
            {
                prepared.Shown.Should().BeFalse("the final resolver must join before any native publication");
                prepared.Activations.Should().Be(0);
            }
            prepared.State.ActiveText.Should().Be(content.Source)
                .And.NotContain(message.Body).And.NotContain(message.AssistantResponse!.Body)
                .And.NotContain("earlier private source body marker");
            prepared.State.CanInteractWithFinalDocument.Should().BeFalse();
            await f.Controller.CopyAsync(content.Reference, generation, true);
            f.Clipboard.Writes.Should().BeEmpty();
            if (change is "availability-lost") { availability = SubmittedHistoryAvailability.Unavailable; }
            if (change is "availability-restored") { availability = SubmittedHistoryAvailability.RetainedRevision; }
            if (change is "origin") { assistantOrigin = true; }
            if (change is "private") { f.Accessible = false; }
            if (change is "selection") { admitted = false; }
            if (change is "session") { f.Controller.RevokeSession(message.Request.SessionId); }
        }
        finally
        {
            release.TrySetResult();
            if (phase is "publication" && change is not "unchanged")
            {
                await operation.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("Exact conversation detail admission changed at final publication.");
            }
            else { await operation; }
        }
        prepared.Should().NotBeNull();
        var view = prepared ?? throw new InvalidOperationException("No exact recorded-context viewer was prepared.");
        calls.Should().Be(phase is "publication" ? 2 : 3);
        f.Views.Should().ContainSingle("availability or origin changes must not automatically open a replacement source");
        if (change is "unchanged")
        {
            view.Cleared.Should().BeFalse();
            view.Shown.Should().BeTrue();
            view.Activations.Should().Be(1);
            view.State.Content!.IsSameSnapshot(content).Should().BeTrue();
            view.State.ActiveText.Should().Be(content.Source);
        }
        else
        {
            view.Cleared.Should().BeTrue();
            view.State.Content.Should().BeNull();
            view.State.ActiveText.Should().BeEmpty();
            view.State.SearchQuery.Should().BeEmpty();
            view.State.CompleteRender(generation, "late private source body", "Rendered", false).Should().BeFalse();
            if (phase is "publication") { view.Shown.Should().BeFalse(); }
        }
        f.Accessible.Should().Be(change is not "private");
        admitted.Should().Be(change is not "selection");
        await f.Controller.CopyAsync(content.Reference, generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
        message.SubmittedContext!.Sources.Should().ContainSingle();
    }

    private static ConversationMessage RecordedContextAnswer()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var source = new ConversationMessage(new(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            new(1), 1, 2, DateTimeOffset.UnixEpoch, "earlier private source body marker",
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var envelope = ConversationHistoryContext.CreateEnvelope(request, "retained original user body marker",
            [new(source, ConversationHistoryRole.User)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15));
        return new(request, new(1), 2, 3, DateTimeOffset.UnixEpoch, envelope.UserRequest,
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, "retained assistant body marker"),
            SubmittedContext = SubmittedAnswerContext.Capture(provenance, new(1),
                new(ProviderService.Local, new("device"), new(1), new("model"), new("default"), new("default")),
                envelope, envelope.Serialize(), DateTimeOffset.UnixEpoch),
        };
    }

    private static AdmittedDetailContent RecordedContextContent(ConversationMessage message, SubmittedHistoryAvailability availability)
    {
        ImmutableArray<SubmittedHistoryAvailability> states = [availability];
        return new(AnswerContextInspection.Reference(message, states), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedAnswerContext, DetailSensitivity.DisclosureConfirmationRequired,
            "Context submitted for this answer", "Host-recorded historical metadata", AnswerContextInspection.Describe(message, states),
            conversationMessage: message, contextAvailability: states);
    }
}
