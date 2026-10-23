using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
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

    private static SessionConversationService QuestionService(Fixture f, ModelTurnHost host, ISecurityAuditLog? audit = null)
    {
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        return new(f, f, workspace, f, host, f.Time, f, execution: host, cancellationAudit: audit ?? f, interactions: f.Questions);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ExactPendingQuestionCancellationJoinsLatePresenterAndRetainsOnlyCancelledTerminal(
        RequestOrigin channel, bool lateFault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var audit = new QuestionCancellationAudit();
        var service = QuestionService(f, host, audit);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        var phases = new List<ConversationExecutionPhase>();
        service.RunningConversationChanged += status => phases.Add(status.Phase);
        var submission = service.SubmitAsync(target, "one original question request", static () => true, Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var created = (await new HostQuestionService(f.Questions, f.Time).CreateAsync(request, spec, expiry, token)).Question!;
                shown.TrySetResult();
                await release.Task;
                if (lateFault) { throw new IOException("private late question body must not be logged"); }
                return new(created, new(HostInteractionOutcome.Answered, "too-late-receipt",
                    created with { Key = created.Key.Next(), Status = QuestionStatus.Answered,
                        Draft = new(["option-0"]), AnswerChannel = channel }));
            });
        ConversationCancellationTarget? observed = null;
        try
        {
            await shown.Task.WaitAsync(Token);
            observed = (await service.ReadRunningConversationAsync(target, static () => true, Token))!.Target;
            (await service.CancelRunningConversationAsync(observed, static () => true, Token, channel))
                .Phase.Should().Be(ConversationExecutionPhase.Cancelling);
            (await service.CancelRunningConversationAsync(observed, static () => true, Token, channel))
                .Phase.Should().Be(ConversationExecutionPhase.Cancelling);
            submission.IsCompleted.Should().BeFalse("question presentation must actually quiesce before terminal retention");
            host.IsQuiescent.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
            audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            audit.Requests.Should().OnlyContain(request => request.TaskId == observed.Request.TaskId
                && request.SessionId == observed.Request.SessionId && request.Origin == channel
                && request.RequestId != observed.Request.RequestId);
        }
        finally { release.TrySetResult(); }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.Reason.Should().Be(ModelTurnReason.CallerCancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown, "dispatched cancellation is not a rollback claim");
        phases.Last().Should().Be(ConversationExecutionPhase.Terminal);
        f.ExecutionCompletions.Should().Be(1);
        transport.Requests.Should().ContainSingle();
        f.Questions.Commits.Should().NotContain(item => item.Audit.ActionId == "question.continue");
        f.Messages.Should().NotContain(text => text.Contains("private late", StringComparison.Ordinal));
        (await service.ReadRunningConversationAsync(target, static () => true, Token)).Should().BeNull();
        await service.Invoking(value => value.CancelRunningConversationAsync(observed!, static () => true, Token, channel))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("before-send")]
    [InlineData("after-answer")]
    [InlineData("after-final")]
    [InlineData("commit-wins")]
    public async Task QuestionUsesCanonicalCancellationWinnerBeforeSendDuringResumeAndBeforeTerminalCommit(string boundary)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var service = QuestionService(f, host, new QuestionCancellationAudit());
        Task<ConversationExecutionStatus>? cancel = null;
        ConversationCancellationTarget? captured = null;
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == ConversationExecutionPhase.Running) { captured = status.Target; }
            if (boundary is "before-send" && status.Phase == ConversationExecutionPhase.Running
                || boundary is "commit-wins" && status.Phase == ConversationExecutionPhase.Completing)
            { cancel = service.CancelRunningConversationAsync(status.Target, static () => true, Token); }
        };
        transport.ChangeReply = (request, reply) =>
        {
            if (boundary is "after-final" && request.Round == 2)
            {
                cancel = service.CancelRunningConversationAsync(captured!, static () => true, Token);
            }
            return reply;
        };
        var receipt = await service.SubmitAsync(new(f.Session, null), "exact original", static () => true, Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var initial = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                var completed = await questions.SubmitAsync(initial.Key, new(["option-0"]), RequestOrigin.LocalUi, token);
                if (boundary is "after-answer")
                {
                    var running = (await service.ReadRunningConversationAsync(new(f.Session, null), static () => true, Token))!;
                    cancel = service.CancelRunningConversationAsync(running.Target, static () => true, Token);
                    await cancel;
                }
                return new(initial, completed);
            });
        cancel.Should().NotBeNull();
        if (boundary is "commit-wins")
        {
            Func<Task> cancelled = async () => { await cancel!; };
            await cancelled.Should().ThrowAsync<InvalidOperationException>();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        }
        else
        {
            await cancel!;
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        f.ExecutionCompletions.Should().Be(1);
        transport.Requests.Should().HaveCount(boundary is "before-send" ? 0 : boundary is "after-answer" ? 1 : 2);
    }

    private sealed class QuestionCancellationAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        internal List<HostRequest> Requests { get; } = [];
        internal SecurityAuditOutcome? FailOnOutcome { get; init; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == FailOnOutcome) { throw new IOException("Required cancellation audit unavailable"); }
            Events.Add(auditEvent);
            Requests.Add(HostActivity.RequireCurrent().Request);
        }
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task FailedRequiredCancellationAuditNeverSignalsPendingQuestionOrClaimsCancellation(SecurityAuditOutcome fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var audit = new QuestionCancellationAudit { FailOnOutcome = fault };
        var service = QuestionService(f, host, audit);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitAsync(source, "one exact request", static () => true, Token,
            questionPresenter: async (request, _, spec, expiry, _, token) =>
            {
                f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
                var questions = new HostQuestionService(f.Questions, f.Time);
                var created = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
                shown.TrySetResult();
                await answerNow.Task;
                token.IsCancellationRequested.Should().BeFalse();
                var submitted = await questions.SubmitAsync(created.Key, new(["option-0"]), RequestOrigin.LocalUi, token);
                return new(created, submitted);
            });
        try
        {
            await shown.Task.WaitAsync(Token);
            var running = (await service.ReadRunningConversationAsync(source, static () => true, Token))!;
            await service.Invoking(value => value.CancelRunningConversationAsync(running.Target, static () => true, Token))
                .Should().ThrowAsync<IOException>();
            (await service.ReadRunningConversationAsync(source, static () => true, Token))!.Phase
                .Should().Be(ConversationExecutionPhase.Running);
            submission.IsCompleted.Should().BeFalse();
        }
        finally { answerNow.TrySetResult(); }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded, "the rejected cancellation did not replace a fresh explicit answer");
        f.ExecutionCompletions.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
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
