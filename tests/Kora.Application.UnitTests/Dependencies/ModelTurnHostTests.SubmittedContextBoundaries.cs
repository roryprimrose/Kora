using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(32768)]
    [InlineData(32769)]
    public async Task SubmittedContextCompleteCellBudgetRetainsExactLimitButConvertsOverflowToUnknownWithoutPartialSuccess(int bytes)
    {
        using var f = new Fixture();
        var selections = Enumerable.Range(1, 8).Select(sequence =>
        {
            var source = Earlier(f, sequence) with { Body = "earlier source marker" };
            f.HistoryMessages[source.Request.RequestId] = source;
            return new ConversationHistorySelection(source, ConversationHistoryRole.User);
        }).ToArray();
        var option = new string('<', 128);
        var profile = new ProviderProfileTuple(ProviderService.Local, new(option), new(1), new(option), new(option), new(option));
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.Account = option;
        f.ProfileCatalogue.Model = option;
        f.ProfileCatalogue.Reasoning = option;
        f.ProfileCatalogue.Context = option;
        f.ProfilePreferences.Value = new(ProviderService.Local, [profile]);
        var host = f.ConfiguredProfiles();
        var service = f.ConversationService(host, execution: host);
        const string marker = "original private user marker:";
        var body = marker + new string('\u0800', ConversationText.MaximumUtf16Characters - marker.Length);
        using var review = await service.ReviewHistoryAsync(new(f.Session, null), body, selections, static () => true, Token);
        ConversationMessage? prospective = null;
        f.Adapter.OnRun = () =>
        {
            var provenance = f.Adapter.Provenance!;
            var metadata = new SubmittedAnswerContext(1, provenance, f.Session.Generation, f.Adapter.Profile!,
                f.Time.Now, f.Adapter.Wire!.Length, [.. selections.Select(item => new SubmittedHistorySource(
                    item.Message.Request.RequestId, item.Message.Generation, item.Message.Sequence, item.Message.Revision, item.Role))]);
            var cell = f.Retained! with
            {
                Revision = 3, Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
                AssistantResponse = new(provenance, "a"), SubmittedContext = metadata,
            };
            var framing = JsonSerializer.SerializeToUtf8Bytes(cell).Length - 1;
            var answer = new string('a', bytes - framing);
            prospective = cell with { AssistantResponse = new(provenance, answer) };
            ConversationText.Validate(body, submission: true);
            prospective.AssistantResponse!.Validate();
            metadata.Validate();
            metadata.CanonicalUtf8Bytes.Should().BeLessThan(ModelContextEnvelope.MaximumInputUtf8Bytes);
            JsonSerializer.SerializeToUtf8Bytes(prospective).Length.Should().Be(bytes);
            JsonSerializer.SerializeToUtf8Bytes(prospective with { SubmittedContext = null }).Length
                .Should().BeLessThan(ConversationMessage.MaximumStorageUtf8Bytes);
            f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null));
        };
        var receipt = await service.SubmitHistoryAsync(review, static () => true, Token);
        prospective.Should().NotBeNull("the qualified fake adapter must actually receive the canonical history envelope");
        var expected = prospective ?? throw new InvalidOperationException("The canonical adapter handoff did not occur.");
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.Should().Be(expected.Request);
        receipt.Message.Execution.Should().Be(expected.Execution);
        receipt.Message.Sequence.Should().Be(9);
        receipt.Message.Revision.Should().Be(3);
        receipt.Task.Revision.Value.Should().Be(3);
        if (bytes == ConversationMessage.MaximumStorageUtf8Bytes)
        {
            receipt.Message.Should().Be(expected);
            receipt.Message.Validate();
            JsonSerializer.SerializeToUtf8Bytes(receipt.Message).Length.Should().Be(32768);
            receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        }
        else
        {
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            receipt.Message.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.SubmittedContext.Should().BeNull();
            receipt.Task.State.Should().Be(HostTaskState.Unknown);
            receipt.Message.Validate();
            f.Retained.Should().Be(receipt.Message);
            f.Task!.Task.Should().Be(receipt.Task);
        }
        f.Messages.Should().NotContain(text => text.Contains(body, StringComparison.Ordinal)
            || text.Contains(marker, StringComparison.Ordinal)
            || text.Contains(expected.AssistantResponse!.Body, StringComparison.Ordinal)
            || text.Contains("earlier source marker", StringComparison.Ordinal));
        review.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedContextAvailabilityChangesAcrossAwaitedApplicationObservationsDenyPublicationWithoutRenewingAuthority(bool restore)
    {
        using var f = new Fixture();
        var source = Earlier(f);
        var host = QualifiedHistoryHost(f);
        var service = f.ConversationService(host, execution: host);
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        using var review = await service.ReviewHistoryAsync(target, "follow-up", [new(source, ConversationHistoryRole.User)],
            static () => true, Token);
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("retained reply body marker", null));
        var retained = await service.SubmitHistoryAsync(review, static () => true, Token);
        retained.Message.SubmittedContext.Should().NotBeNull();
        if (restore) { f.HistoryMessages.Remove(source.Request.RequestId).Should().BeTrue(); }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeSubmittedAvailabilityRead = async token =>
        {
            if (f.SubmittedAvailabilityReads != 2) { return; }
            entered.SetResult();
            await release.Task.WaitAsync(token);
        };
        var audit = f.Audits.ToArray();
        var control = f.ControlRevision;
        var sourceReads = f.HistoryReads;
        var published = new List<AdmittedDetailContent>();
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        async Task Inspect()
        {
            published.Add(await service.ReadDetailAsync(target, retained.Message, static () => true, Token, submittedContext: true));
        }
        var inspection = Inspect();
        try
        {
            await entered.Task.WaitAsync(Token);
            published.Should().BeEmpty();
            if (restore) { f.HistoryMessages.Add(source.Request.RequestId, source); }
            else { f.HistoryMessages.Remove(source.Request.RequestId).Should().BeTrue(); }
        }
        finally
        {
            release.TrySetResult();
            await inspection.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Conversation detail source or private admission changed before publication.");
        }
        published.Should().BeEmpty();
        f.SubmittedAvailabilityReads.Should().Be(2);
        f.HistoryReads.Should().Be(sourceReads, "metadata observation must not read or reconstruct a source body");
        f.BeforeSubmittedAvailabilityRead = null;
        var fresh = await service.ReadDetailAsync(target, retained.Message, static () => true, Token, submittedContext: true);
        fresh.Reference.Should().Be(AnswerContextInspection.Reference(retained.Message,
            [restore ? SubmittedHistoryAvailability.RetainedRevision : SubmittedHistoryAvailability.Unavailable]));
        fresh.Source.Should().Contain(restore ? "exact source-row revision is retained" : "missing or changed")
            .And.NotContain(source.Body).And.NotContain(source.AssistantResponse!.Body)
            .And.NotContain(retained.Message.Body).And.NotContain(retained.Message.AssistantResponse!.Body);
        var state = new Kora.Application.Presentation.DetailViewerState(fresh, static () => true);
        state.TryGetCopySource(true, true, out _).Should().BeFalse();
        state.CanInteractWithFinalDocument.Should().BeFalse();
        f.Audits.Should().Equal(audit);
        f.ControlRevision.Should().Be(control);
        f.Session.Should().Be(target.Authority);
        f.Retained.Should().Be(retained.Message);
        f.Task!.Task.Should().Be(retained.Task);
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        f.Messages.Should().NotContain(text => text.Contains(source.Body, StringComparison.Ordinal)
            || text.Contains("earlier ASSISTANT", StringComparison.Ordinal));
    }
}
