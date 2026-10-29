using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedQuestionReusesCanonicalLocalQuestionOwnerAndRevalidatesBeforeAnswerCommit(bool revokeAtCommit)
    {
        using var f = new Fixture();
        await using var settings = new OrdinaryDeadlineConfigurationFixture();
        settings.Preferences.BeginWrite();
        var preferenceReads = 0;
        settings.Store.Before = (_, _) => preferenceReads++;
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.ClaudeProfiles(new ClaudeRuntime(observations: true), reviews);
        await using var service = DeadlineConversation(f, host, settings);
        reviews.ClaudeOffered += review =>
        { using var presentation = review.Presentation(); reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue(); };
        service.RunningConversationChanged += status =>
        {
            if (revokeAtCommit && status.Phase == ConversationExecutionPhase.Completing)
            { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        };
        var asks = 0;
        var receipt = await service.SubmitClaudeWithClarificationAsync(new(f.Session, null), "original hosted question",
            () => true, async (request, generation, spec, expiry, _, token) =>
            {
                asks++;
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var initial = (await new Kora.Application.Interaction.HostQuestionService(f.Questions, f.Time)
                    .CreateAsync(request, spec, expiry, token)).Question!;
                initial.SessionGeneration.Should().Be(generation);
                var submitted = await new Kora.Application.Interaction.HostQuestionService(f.Questions, f.Time)
                    .SubmitAsync(initial.Key, new(["option-1"]), RequestOrigin.ActivatedVoice, token);
                return new(initial, submitted);
            }, Token);
        asks.Should().Be(1);
        receipt.Message.Body.Should().Be("original hosted question");
        receipt.Message.AssistantResponse?.Body.Should().Be(revokeAtCommit ? null : "The observed version is 1.0.");
        receipt.Message.Outcome.Should().Be(revokeAtCommit ? ModelTurnOutcome.DeniedEgress : ModelTurnOutcome.Succeeded);
        f.ExecutionCompletions.Should().Be(1);
        f.Questions.Commits.Last().Audit.ActionId.Should().Be("question.continue");
        f.Questions.Snapshot.Grants.Should().BeEmpty();
        preferenceReads.Should().Be(0, "hosted clarification keeps its fixed user-wait budget, independent of ordinary preferences");
    }

    [Fact]
    public async Task HostedQuestionCannotEnterLocalAdmissionOrCopilotPreparationOrStreaming()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var context = f.Context(toolMode: ModelToolMode.HostedClarification);
        var denied = await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token);
        denied.Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
        denied.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        Assert.Throws<InvalidOperationException>(() => reviews.PrepareTurn(context, Profile(ProviderService.Copilot)));
        Assert.Throws<InvalidOperationException>(() => reviews.PrepareClaudeTurn(context, Profile(ProviderService.Claude), streamAnswer: true));
        var host = f.ClaudeProfiles(new ClaudeRuntime(), reviews);
        (await host.AdmitClaudeAsync(context, Token, streamAnswer: true)).Turn.Should().BeNull();
    }

    [Fact]
    public async Task HostedQuestionWithoutBoundNativeOwnerCannotExecuteOrSend()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new ClaudeRuntime();
        var host = f.ClaudeProfiles(runtime, new(f, f.Time));
        var admission = await host.AdmitClaudeAsync(f.Context(toolMode: ModelToolMode.HostedClarification), Token);
        admission.Turn.Should().NotBeNull();
        var result = await host.RunAsync(admission.Turn!, Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        runtime.Calls.Should().Be(0);
    }
}
