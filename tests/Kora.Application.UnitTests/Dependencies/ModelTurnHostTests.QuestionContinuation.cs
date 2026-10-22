using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string Clarification = """{"kind":"question","prompt":"Which format?","options":["Compact","Detailed"]}""";

    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    public async Task ExactQuestionAndFreshOriginalAnswerResumeOneRealTurnAndRetainOnlyGenuineFinalResponse(RequestOrigin original, RequestOrigin channel)
    {
        using var f = new Fixture(original);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var service = QuestionService(f, host);
        var presentations = 0;
        var receipt = await service.SubmitAsync(new(f.Session, null), "original user request", static () => true, Token,
            origin: original, questionPresenter: async (request, generation, spec, expiry, _, token) =>
            {
                presentations++;
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var created = await questions.CreateAsync(request, spec, expiry, token);
                var reviewed = await new HostQuestionReviewService(f.Questions, f.Time).ReviewAsync(created.Question!.Key, token);
                reviewed.Question!.SessionGeneration.Should().Be(generation);
                var drafted = await questions.DraftAsync(created.Question.Key, new(["option-0"]), channel, token);
                drafted.Outcome.Should().Be(HostInteractionOutcome.Drafted);
                var submitted = await questions.SubmitAsync(drafted.Question!.Key, new(["option-1"]), channel, token);
                return new(created.Question, submitted);
            });
        presentations.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(request => request.ClarificationAllowed
            && request.Context.ToolMode == ModelToolMode.None && request.Provenance == receipt.Message.Execution);
        var answer = transport.Requests[1].Messages[1].Should().BeOfType<ModelQuestionAnswerMessage>().Subject;
        answer.Answer.Choices.Should().Equal("option-1");
        answer.Channel.Should().Be(channel);
        answer.Question.Should().BeSameAs(transport.Requests[1].Messages[0]);
        receipt.Message.Request.Origin.Should().Be(original);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        receipt.Message.Body.Should().Be("original user request");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        f.ExecutionCompletions.Should().Be(1);
        f.Questions.Commits.Last().Audit.ActionId.Should().Be("question.continue");
        f.Messages.Should().NotContain(message => message.Contains("Which format", StringComparison.Ordinal)
            || message.Contains("original user request", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("draft")]
    [InlineData("audit")]
    [InlineData("foreign")]
    [InlineData("repeat")]
    [InlineData("provider-failure")]
    [InlineData("bad-final")]
    [InlineData("credential")]
    public async Task NonAnswersAndFailedContinuationNeverReplayAndRetainExplicitTerminalWithoutAssistant(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var final = fault switch
        {
            "repeat" => Clarification,
            "bad-final" => """{"kind":"answer","answer":"api_key=synthetic-only-value"}""",
            _ => FinalAnswer,
        };
        var transport = new ToolTransport(Clarification, final);
        if (fault is "provider-failure")
        {
            transport.ChangeReply = (request, reply) => request.Round == 2
                ? new(ModelTurnOutcome.Unavailable, request.Provenance) : reply;
        }
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var receipt = await QuestionService(f, host).SubmitAsync(new(f.Session, null), "original request", static () => true, Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var created = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                var completed = fault switch
                {
                    "cancel" => await questions.CancelAsync(created.Key, token),
                    "draft" => await questions.DraftAsync(created.Key, new(["option-0"]), RequestOrigin.LocalUi, token),
                    _ => await questions.SubmitAsync(created.Key, new(["option-0"]), RequestOrigin.LocalUi, token),
                };
                if (fault is "foreign")
                {
                    var answered = completed.Question!;
                    completed = completed with { Question = answered with { Key = new(request, new(Guid.NewGuid()), answered.Key.Revision) } };
                }
                if (fault is "credential")
                { completed = completed with { Question = completed.Question! with { Draft = new([], "api_key=synthetic-only-value") } }; }
                if (fault is "audit") { f.Questions.FailAudit = true; }
                return new(created, completed);
            });
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.IsTerminal.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(1);
        transport.Requests.Should().HaveCount(fault is "repeat" or "provider-failure" or "bad-final" ? 2 : 1);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("expiry")]
    [InlineData("caller-cancel")]
    public async Task DriftOrCancellationDuringQuestionCannotPublishAnyLateAnswer(string fault)
    {
        using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var operation = () => QuestionService(f, host).SubmitAsync(new(f.Session, null), "original request", static () => true, cancellation.Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var initial = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                var completed = await questions.SubmitAsync(initial.Key, new(["option-0"]), RequestOrigin.LocalUi, token);
                if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
                if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
                if (fault is "privacy") { f.CanControl = false; }
                if (fault is "control") { f.ControlRevision++; }
                if (fault is "expiry") { f.Time.Now = expiry; }
                if (fault is "caller-cancel") { cancellation.Cancel(); }
                return new(initial, completed);
            });
        if (fault is "expiry")
        {
            var receipt = await operation();
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Task.IsTerminal.Should().BeTrue();
        }
        else
        {
            await operation.Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        await ((IModelTurnExecution)host).WaitForQuiescenceAsync(Token);
        transport.Requests.Should().ContainSingle();
    }

    private static SessionConversationService QuestionService(Fixture f, ModelTurnHost host)
    {
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        return new(f, f, workspace, f, host, f.Time, f, host, f.Questions);
    }

    [Fact]
    public async Task CancellationDuringNoncooperativeQuestionWaitKeepsOwnershipUntilActualExitAndSuppressesLateResume()
    {
        using var f = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = QuestionService(f, host).SubmitAsync(new(f.Session, null), "original request", static () => true, cancellation.Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var question = (await new HostQuestionService(f.Questions, f.Time).CreateAsync(request, spec, expiry, token)).Question!;
                shown.TrySetResult();
                await release.Task;
                return new(question, new(HostInteractionOutcome.Answered, "late-untrusted-receipt",
                    question with { Key = question.Key.Next(), Status = QuestionStatus.Answered,
                        Draft = new(["option-0"]), AnswerChannel = RequestOrigin.LocalUi }));
            });
        try
        {
            await shown.Task.WaitAsync(Token);
            await cancellation.CancelAsync();
            Func<Task> completion = async () => { await run; };
            await completion.Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
            host.IsQuiescent.Should().BeFalse();
            loop.IsQuiescent.Should().BeFalse();
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        finally
        {
            release.TrySetResult();
            await ((IModelTurnExecution)host).WaitForQuiescenceAsync(Token);
        }
        host.IsQuiescent.Should().BeTrue();
        loop.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(0);
        transport.Requests.Should().ContainSingle();
        f.Questions.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue");
    }
}
