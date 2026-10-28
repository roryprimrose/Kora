using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task ReadOnlyClarificationResumesOriginalRetainedTaskAndExecutesCanonicalToolsBeforeActualAnswer(
        bool toolFirst, RequestOrigin channel)
    {
        using var f = new Fixture(channel);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var script = toolFirst
            ? new[] { VersionCall, Clarification, VersionCall.Replace("version_1", "version_3", StringComparison.Ordinal), FinalAnswer }
            : [Clarification, VersionCall, FinalAnswer];
        var transport = new ToolTransport(script);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var reads = 0;
        f.OnVersion = () => reads++;
        var service = QuestionService(f, host);
        var receipt = await service.SubmitReadOnlyWithClarificationAsync(new(f.Session, null), "Which observation do I need?",
            static () => true, (request, _, spec, expiry, _, token) => AnswerReadOnlyQuestion(f, request, spec, expiry, channel, token),
            Token, origin: channel);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed version is 1.0.");
        receipt.Message.Body.Should().Be("Which observation do I need?");
        receipt.Message.Request.Origin.Should().Be(channel);
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        reads.Should().Be(toolFirst ? 2 : 1);
        transport.Requests.Should().HaveCount(toolFirst ? 4 : 3);
        transport.Requests.Select(request => request.Round).Should().Equal(Enumerable.Range(1, transport.Requests.Count));
        transport.Requests.Should().OnlyContain(request => request.Provenance == receipt.Message.Execution
            && request.Context.ToolMode == ModelToolMode.ReadOnly && request.ClarificationAllowed);
        var final = transport.Requests[^1];
        var answer = final.Messages.OfType<ModelQuestionAnswerMessage>().Single();
        answer.Answer.Choices.Should().Equal("option-0");
        answer.Channel.Should().Be(channel);
        final.Messages.OfType<ModelToolResultMessage>().Should().HaveCount(reads);
        transport.Requests.Zip(transport.Requests.Skip(1)).Should().OnlyContain(pair =>
            pair.Second.Budget.InputUtf8Bytes < pair.First.Budget.InputUtf8Bytes
            && pair.Second.Budget.OutputUtf8Bytes < pair.First.Budget.OutputUtf8Bytes);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
        f.Questions.Snapshot.Questions.Should().ContainSingle().Which.Proposal.Should().BeNull();
        f.Questions.Commits.Should().Contain(commit => commit.Audit.ActionId == "question.continue");
        f.ExecutionStarts.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("reused-call")]
    [InlineData("free-text")]
    [InlineData("mixed")]
    [InlineData("approval")]
    [InlineData("unknown-field")]
    [InlineData("cancel")]
    [InlineData("draft")]
    [InlineData("foreign-origin")]
    [InlineData("expired")]
    [InlineData("answer-budget")]
    [InlineData("round-budget")]
    public async Task ReadOnlyQuestionCannotGrantAuthorityReplayOrResetTheBudget(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var question = fault switch
        {
            "free-text" => """{"kind":"question","prompt":"Which observation?","options":[]}""",
            "mixed" => """{"kind":"question","prompt":"Choose","options":["Version","Runtime"],"calls":[]}""",
            "approval" => """{"kind":"grantChange","approved":true}""",
            "unknown-field" => """{"kind":"question","prompt":"Choose","options":["Version","Runtime"],"session":"foreign"}""",
            _ => Clarification,
        };
        var script = fault is "round-budget"
            ? new[] { VersionCall, question }.Concat(Enumerable.Range(3, 5)
                .Select(round => VersionCall.Replace("version_1", "version_" + round, StringComparison.Ordinal))).ToArray()
            : [VersionCall, question, fault is "repeat" ? Clarification : fault is "reused-call" ? VersionCall : FinalAnswer];
        var transport = new ToolTransport(script);
        if (fault is "answer-budget")
        {
            transport.ChangeReply = (request, reply) => request.Round == 2
                ? reply with { Usage = reply.Usage! with { OutputUtf8Bytes = request.Budget.OutputUtf8Bytes - 1 } } : reply;
        }
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var reads = 0;
        var presentations = 0;
        f.OnVersion = () => reads++;
        var receipt = await QuestionService(f, host).SubmitReadOnlyWithClarificationAsync(new(f.Session, null),
            "one original read-only request", static () => true, async (request, _, spec, expiry, _, token) =>
            {
                presentations++;
                var presentation = await AnswerReadOnlyQuestion(f, request, spec, expiry, RequestOrigin.LocalUi, token,
                    fault is "cancel" or "draft" ? fault : null);
                if (fault is "foreign-origin")
                {
                    var completed = presentation.Completed;
                    var answered = completed.Question!;
                    return new(presentation.Presented, completed with
                    {
                        Question = answered with { Key = new(new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.ActivatedVoice),
                            answered.Key.QuestionId, answered.Key.Revision) },
                    });
                }
                if (fault is "expired") { f.Time.Now = expiry; }
                return presentation;
            }, Token);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        reads.Should().Be(fault is "round-budget" ? 6 : 1);
        presentations.Should().Be(fault is "free-text" or "mixed" or "approval" or "unknown-field" ? 0 : 1);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task ReadOnlyQuestionRequiresTheActualPresenter()
    {
        using var f = new Fixture();
        var service = QuestionService(f, f.Host);
        var submit = () => service.SubmitReadOnlyWithClarificationAsync(new(f.Session, null), "original",
            static () => true, null!, Token);
        await submit.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("device-source")]
    [InlineData("call-source")]
    [InlineData("profile")]
    [InlineData("expiry")]
    public async Task ReadOnlyQuestionRevalidatesOriginalSourceSessionAndProfileBeforeAnyResumedTool(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(VersionCall, Clarification,
            VersionCall.Replace("version_1", "version_3", StringComparison.Ordinal), FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var sourceCurrent = true;
        var reads = 0;
        f.OnVersion = () => reads++;
        var operation = () => QuestionService(f, host).SubmitReadOnlyWithClarificationAsync(new(f.Session, null),
            "original observation request", static () => true, async (request, _, spec, expiry, _, token) =>
            {
                var answer = await AnswerReadOnlyQuestion(f, request, spec, expiry, RequestOrigin.LocalUi, token);
                switch (fault)
                {
                    case "generation": f.Session = f.Session with { Generation = new(2) }; break;
                    case "metadata": f.Metadata = new(f.Session.SessionId, new(1), new("changed")); break;
                    case "privacy": f.CanControl = false; break;
                    case "control": f.ControlRevision++; break;
                    case "profile": f.SessionProfileHeld = true; break;
                    case "expiry": f.Time.Now = expiry; break;
                    default: sourceCurrent = false; break;
                }
                return answer;
            }, Token, validateSource: _ => sourceCurrent ? Task.CompletedTask
                : Task.FromException(new IOException("Original input source epoch retired.")));
        if (fault is "profile" or "expiry")
        {
            var receipt = await operation();
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Denied);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            await operation.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            f.Retained!.AssistantResponse.Should().BeNull();
        }
        await ((IModelTurnExecution)host).WaitForQuiescenceAsync(Token);
        reads.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReadOnlyQuestionCancellationRetainsPhysicalOwnershipUntilLatePresenterActuallyJoins(RequestOrigin channel)
    {
        using var f = new Fixture(channel);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var transport = new ToolTransport(VersionCall, Clarification, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.ConfiguredProfiles(adapter: loop);
        var service = QuestionService(f, host, new QuestionCancellationAudit());
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        var submission = service.SubmitReadOnlyWithClarificationAsync(target, "original request", static () => true,
            async (request, _, spec, expiry, _, token) =>
            {
                var answer = await AnswerReadOnlyQuestion(f, request, spec, expiry, channel, token);
                shown.TrySetResult();
                await release.Task;
                return answer;
            }, Token, origin: channel);
        try
        {
            await shown.Task.WaitAsync(Token);
            var running = (await service.ReadRunningConversationAsync(target, static () => true, Token))!;
            var cancelled = await service.CancelRunningConversationAsync(running.Target, static () => true, Token, channel);
            cancelled.Phase.Should().Be(ConversationExecutionPhase.Cancelling);
            submission.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            loop.IsQuiescent.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
        }
        finally { release.TrySetResult(); }
        var receipt = await submission;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.State.Should().Be(HostTaskState.Unknown);
        transport.Requests.Should().HaveCount(2);
        f.Questions.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue");
        host.IsQuiescent.Should().BeTrue();
        loop.IsQuiescent.Should().BeTrue();
    }

    private static async Task<ConversationQuestionPresentation> AnswerReadOnlyQuestion(Fixture f, HostRequest request,
        QuestionSpec spec, DateTimeOffset expiry, RequestOrigin channel, CancellationToken token, string? fault = null)
    {
        f.Questions.Change(snapshot => snapshot with { Intent = f.Task!.Task, Session = f.Session });
        spec.Kind.Should().Be(QuestionKind.SingleChoice);
        var questions = new HostQuestionService(f.Questions, f.Time);
        var initial = (await questions.CreateAsync(request, spec, expiry, token)).Question!;
        var completed = fault switch
        {
            "cancel" => await questions.CancelAsync(initial.Key, token),
            "draft" => await questions.DraftAsync(initial.Key, new(["option-0"]), channel, token),
            _ => await questions.SubmitAsync(initial.Key, new(["option-0"]), channel, token),
        };
        return new(initial, completed);
    }
}
