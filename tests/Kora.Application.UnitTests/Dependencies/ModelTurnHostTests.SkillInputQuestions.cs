using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true, false)]
    [InlineData(RequestOrigin.LocalUi, false, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false, false)]
    [InlineData(RequestOrigin.LocalUi, true, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true, true)]
    public async Task MissingSkillInputsAskExactQuestionsThenPreviewAndSeparateUseReachCanonicalFinal(
        RequestOrigin origin, bool qualified, bool separateInput)
    {
        using var f = new Fixture(origin);
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(2);
        await using var capture = new QuestionReplyFixture.ReplyCapture();
        OwnedSkillQuestionContinuation? Continuation() => origin == RequestOrigin.ActivatedVoice
            ? new(() => capture.Generation == 0, static () => true, () => capture.Generation) : null;
        await using var skill = new OwnedSkillHarness(f, origin, qualified, questions: Continuation());
        Parameterize(skill, "Compare ${input:topic} with ${input:other}; ${input:topic}.");
        using var review = await skill.Review();
        var asked = new List<string>();
        var inputAuthority = separateInput
            ? new OwnedSkillAuthoringAuthority(origin, skill.Eligible) { Questions = Continuation() } : skill.Authority;
        var values = await skill.Workflow.CollectInputsAsync(review, new(f.Session, null), "Original unchanged",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["other"] = "Already supplied" },
            inputAuthority, skill.Eligible, async (owner, spec, token) =>
            {
                asked.Add(spec.Text);
                spec.Purpose.Should().Be(OwnedSkillInputQuestions.Purpose);
                spec.MaximumTextLength.Should().Be(512);
                f.Adapter.Calls.Should().Be(0);
                f.RetainCalls.Should().Be(0);
                review.IsEnabled.Should().BeFalse();
                var questions = new HostQuestionService(owner, f.Time);
                using var activity = HostActivity.BeginRoot(owner.Request, HostActivityLayer.Application, HostOperation.Presentation);
                var shown = (await questions.CreateAsync(owner.Request, spec, owner.ExpiresAt, token)).Question!;
                if (origin == RequestOrigin.LocalUi)
                {
                    return await questions.SubmitAsync(shown.Key, new([], "  literal ${input:never-expanded} e\u0301  "), origin, token);
                }
                HostInteractionDecision? answer = null;
                await using var replies = new HostQuestionVoiceReplyService(questions, capture,
                    new QuestionReplyFixture.ReplyTime { Now = f.Time.Now },
                    new QuestionReplyFixture.ReplyDispatcher(), () => new(new("mic", "Synthetic"), 1, 1, 1),
                    (_, _, _) => Task.CompletedTask, NullLogger<HostQuestionVoiceReplyService>.Instance);
                await replies.OpenAsync(shown, static () => true, decision => answer = decision, false, token);
                capture.Transcript("  literal ${input:never-expanded} e\u0301  ");
                await replies.Quiescence;
                capture.IsCaptureQuiescent.Should().BeTrue();
                return answer!;
            }, Token);
        asked.Should().ContainSingle().Which.Should().Contain("topic");
        f.Time.ExpectedDeadline = TimeSpan.FromSeconds(15);
        values["other"].Should().Be("Already supplied");
        review.IsEnabled.Should().BeFalse();
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Workflow.PrepareAsync(review, new(f.Session, null), "Original unchanged",
            skill.Authority, skill.Eligible, Token, values);
        invocation.Preview.Should().Contain("ExpandedGuidance").And.NotContain("hidden");
        f.Adapter.Calls.Should().Be(0);
        var receipt = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        receipt.Message.Body.Should().Be("Original unchanged");
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.AssistantResponse?.Body.Should().Be(qualified ? "answer" : null);
        f.Adapter.Calls.Should().Be(qualified ? 1 : 0);
        skill.Store.Writes.Should().Be(0);
        skill.Audit.Events.Should().NotContain(item => JsonSerializer.Serialize(item).Contains("literal", StringComparison.Ordinal));
        if (qualified)
        {
            Encoding.UTF8.GetString(f.Adapter.Wire!).Should().Contain("never-expanded");
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("choice")]
    [InlineData("oversized")]
    [InlineData("unicode")]
    [InlineData("secret")]
    [InlineData("channel")]
    [InlineData("foreign")]
    [InlineData("schema")]
    [InlineData("duplicate")]
    [InlineData("late")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("closed")]
    [InlineData("foreign-request")]
    [InlineData("null-answer")]
    public async Task SkillInputQuestionRefusesInvalidStaleOrForeignAnswersWithoutEffects(string fault)
    {
        using var f = new Fixture();
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(2);
        await using var skill = new OwnedSkillHarness(f);
        Parameterize(skill);
        using var review = await skill.Review();
        await skill.Workflow.Invoking(flow => flow.CollectInputsAsync(review, new(f.Session, null), "Original",
            new Dictionary<string, string>(StringComparer.Ordinal), skill.Authority, skill.Eligible,
            async (owner, spec, token) =>
            {
                var questions = new HostQuestionService(owner, f.Time);
                using var activity = HostActivity.BeginRoot(owner.Request, HostActivityLayer.Application, HostOperation.Presentation);
                var shown = (await questions.CreateAsync(owner.Request, spec, owner.ExpiresAt, token)).Question!;
                if (fault is "null-answer") { return new(HostInteractionOutcome.Answered, "missing-answer"); }
                if (fault is "foreign-request")
                {
                    return await owner.TransactAsync(HostRequest.Create(RequestOrigin.LocalUi),
                        _ => throw new InvalidOperationException("Foreign requests cannot reach a transition."), token);
                }
                if (fault is "foreign")
                {
                    return new(HostInteractionOutcome.Answered, "foreign",
                        shown with { Key = new(shown.Key.Request, new(Guid.NewGuid()), shown.Key.Revision), Draft = new([], "foreign") });
                }

                if (fault is "schema")
                {
                    return await questions.ReviseAsync(shown.Key,
                        new("Other schema", QuestionKind.Text, [], maximumTextLength: 512),
                        owner.ExpiresAt, token);
                }
                if (fault is "duplicate") { return await questions.CreateAsync(owner.Request, spec, owner.ExpiresAt, token); }
                if (fault is "late") { f.Time.Now = review.ExpiresAt; }
                if (fault is "source") { skill.Store.Snapshot = skill.Store.Snapshot! with { InstructionsIdentity = "replaced" }; }
                if (fault is "privacy") { f.CanControl = false; }
                if (fault is "control") { f.ControlRevision++; }
                if (fault is "owner") { skill.Open = false; }
                if (fault is "closed") { owner.Dispose(); }
                if (fault is "cancel") { return await questions.CancelAsync(shown.Key, token); }
                var value = fault switch
                {
                    "empty" => "",
                    "oversized" => new string('a', 513),
                    "unicode" => new string('\ud800', 1),
                    "secret" => "password=" + "synthetic-sensitive-value",
                    _ => "valid",
                };
                return await questions.SubmitAsync(shown.Key,
                    fault is "choice" ? new(["approve"]) : new([], value),
                    fault is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, token);
            }, Token)).Should().ThrowAsync<Exception>();
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillQuestionCollectionRejectsUndeclaredSchemaOrLiteralOldFormat(bool oldFormat)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        if (!oldFormat) { Parameterize(skill); }
        using var review = await skill.Review();
        var calls = 0;
        await skill.Workflow.Invoking(flow => flow.CollectInputsAsync(review, new(f.Session, null), "Original",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["undeclared"] = "value" },
            skill.Authority, skill.Eligible, (_, _, _) =>
            {
                calls++;
                throw new InvalidOperationException("No question should open.");
            }, Token)).Should().ThrowAsync<Exception>();
        calls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("blank")]
    [InlineData("invalid")]
    [InlineData("cancel")]
    [InlineData("intent")]
    public async Task OwnedSkillInputCollectionPreservesReadyFieldsAndRejectsChangedPreparationBeforeAnyModel(string scenario)
    {
        using var f = new Fixture();
        f.Time.ExpectedDeadline = TimeSpan.FromMinutes(2);
        await using var skill = new OwnedSkillHarness(f);
        Parameterize(skill);
        using var review = await skill.Review();
        var initial = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["topic"] = scenario is "blank" ? " " : scenario is "invalid" ? new string('a', 513) : "complete" };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (scenario is "cancel") { cancellation.Cancel(); }
        if (scenario is "intent") { skill.Control.MissingIntent = true; }
        var calls = 0;
        var collecting = skill.Workflow.CollectInputsAsync(review, new(f.Session, null), "Original", initial,
            skill.Authority, skill.Eligible, (_, _, _) =>
            {
                calls++;
                return Task.FromResult(new HostInteractionDecision(HostInteractionOutcome.Cancelled, "cancel"));
            }, cancellation.Token);
        if (scenario is "ready")
        { (await collecting)["topic"].Should().Be("complete"); }
        else { await collecting.Invoking(async task => await task).Should().ThrowAsync<Exception>(); }
        calls.Should().Be(scenario is "blank" ? 1 : 0);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }
}
