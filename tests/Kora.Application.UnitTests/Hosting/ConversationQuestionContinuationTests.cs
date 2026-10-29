using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Hosting;

public sealed class ConversationQuestionContinuationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SpokenQuestionReadsEveryOptionAndBoundsBeforeAcceptingExactAnswers(int selections)
    {
        await using var f = new QuestionReplyFixture();
        var spec = LocalModelQuestionPolicy.CreateSpec(new("Which details?", selections == 0 ? [] : ["Version", "Readiness"],
            1, Math.Max(1, selections)));
        var question = await f.Host.QuestionAsync(spec);
        await f.OpenAsync(question, afterPlayback: true);
        f.Spoken.Should().Be(selections == 0 ? "Which details?" : selections == 1
            ? "Which details? Option 1: Version. Option 2: Readiness. Choose one option."
            : "Which details? Option 1: Version. Option 2: Readiness. Choose 1 to 2 options. "
                + "Say option numbers separated by and, or use the checkboxes.");
        f.Capture.Transcript(selections == 0 ? "Exact user text" : selections == 1 ? "option 1" : "option 1 and option 2");
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Choices.Should().Equal(selections == 0 ? [] : selections == 1 ? ["option-0"] : ["option-0", "option-1"]);
        f.Accepted.Question.Draft.Text.Should().Be(selections == 0 ? "Exact user text" : null);
        f.Accepted.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        f.Capture.IsListening.Should().BeFalse();
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ChangedCheckboxBoundsCannotSubstituteThePresentedOrCanonicalAnswer(bool minimum, bool canonical)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
        {
            var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            var submitted = await f.Questions.SubmitAsync(initial.Key, new(["option-0", "option-1"]), RequestOrigin.LocalUi, token);
            var changed = new QuestionSpec(spec.Text, spec.Kind, spec.Options, minimum ? 2 : 1, minimum ? 3 : 2,
                purpose: spec.Purpose, sourceId: spec.SourceId);
            if (canonical)
            {
                f.Store.Change(snapshot => snapshot with { Questions = [submitted.Question! with { Spec = changed }] });
                return new(initial, submitted);
            }
            return new(initial, submitted with { Question = submitted.Question! with { Spec = changed } });
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
        await broker.Invoking(value => value.AskAsync(new("Choose", ["A", "B", "C"], 1, 3), Token))
            .Should().ThrowAsync<Exception>();
        f.Store.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue"
            && commit.Decision.Outcome == HostInteractionOutcome.Answered);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi, "api_key=synthetic-only-value")]
    [InlineData(true, RequestOrigin.LocalUi, "api_key=synthetic-only-value")]
    [InlineData(false, RequestOrigin.ActivatedVoice, "api_key=synthetic-only-value")]
    [InlineData(true, RequestOrigin.ActivatedVoice, "api_key=synthetic-only-value")]
    [InlineData(false, RequestOrigin.LocalUi, "invalid\0content")]
    [InlineData(true, RequestOrigin.ActivatedVoice, "invalid\0content")]
    public async Task ClarificationCredentialOrInvalidTextIsAuditedAsDeniedBeforeDraftOrAnswerStorage(
        bool submitting, RequestOrigin channel, string content)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var spec = LocalModelQuestionPolicy.CreateSpec(new("Describe the format", []));
        var question = (await f.Questions.CreateAsync(f.Request, spec, f.Time.Now.AddSeconds(15), Token)).Question!;
        var denied = submitting
            ? await f.Questions.SubmitAsync(question.Key, new([], content), channel, Token)
            : await f.Questions.DraftAsync(question.Key, new([], content), channel, Token);
        denied.Outcome.Should().Be(HostInteractionOutcome.Denied);
        denied.Reason.Should().Be("clarification-content-refused-before-storage");
        var canonical = f.Store.Snapshot.Questions.Single();
        canonical.Key.Should().Be(question.Key);
        canonical.Status.Should().Be(QuestionStatus.Pending);
        canonical.Draft.Should().BeNull();
        f.Store.Commits.Last().Audit.Outcome.Should().Be(Kora.Core.Auditing.SecurityAuditOutcome.Denied);
        System.Text.Json.JsonSerializer.Serialize(f.Store.Commits.Last().Audit).Should().NotContain(content);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("text")]
    [InlineData("multiple")]
    [InlineData("foreign-generation")]
    [InlineData("not-visible")]
    [InlineData("unavailable-voice")]
    [InlineData("ambiguous")]
    public async Task ActivatedVoiceRequiresFreshExactVisibleAdmittedReplyAndNeverCreatesApproval(string scenario)
    {
        await using var f = new QuestionReplyFixture();
        using var root = HostActivity.BeginRoot(f.Host.Request, HostActivityLayer.Application, HostOperation.Request);
        if (scenario is "not-visible") { f.Visible = false; }
        if (scenario is "unavailable-voice") { f.Admission = null; }
        var broker = new ConversationQuestionContinuation(f.Host.Store, async (request, _, spec, expiry, _, token) =>
        {
            var initial = (await f.Host.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            await f.OpenAsync(initial);
            if (f.Capture.IsListening)
            {
                f.Capture.Transcript(scenario is "text" ? "Exact free text" : scenario is "multiple" ? "option 1 and option 2"
                    : scenario is "ambiguous" ? "approve" : "Detailed",
                    scenario is "foreign-generation" ? f.Capture.Generation - 1 : f.Capture.Generation);
            }
            await f.Replies.Quiescence;
            f.Replies.Retire("Exact test capture ended");
            await f.Replies.Quiescence;
            return new(initial, f.Accepted ?? new(HostInteractionOutcome.Denied, "no-fresh-exact-voice-answer"));
        }, f.Host.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
        var operation = () => broker.AskAsync(new("Which format?", scenario is "text" ? [] : ["Compact", "Detailed"],
            Maximum: scenario is "multiple" ? 2 : 1), Token);
        if (scenario is "exact" or "text" or "multiple")
        {
            var reply = await operation();
            reply.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
            reply.Question.Proposal.Should().BeNull();
            reply.Question.Draft!.Text.Should().Be(scenario is "text" ? "Exact free text" : null);
            reply.Question.Draft.Choices.Should().Equal(scenario is "text" ? [] : scenario is "multiple"
                ? ["option-0", "option-1"] : ["option-1"]);
        }
        else
        {
            await operation.Should().ThrowAsync<InvalidDataException>();
            f.Host.Store.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue");
        }
        f.Capture.IsListening.Should().BeFalse();
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task ExactFreshSubmittedAnswerIsConsumedOnceWithOriginalChannelAndAuditedCorrelation(RequestOrigin channel, bool text)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var presentations = 0;
        var broker = new ConversationQuestionContinuation(f.Store, async (request, generation, spec, expiry, live, token) =>
        {
            presentations++;
            request.Should().Be(f.Request);
            generation.Should().Be(new HostRevision(1));
            live().Should().BeTrue();
            var created = await f.Questions.CreateAsync(request, spec, expiry, token);
            var review = await new HostQuestionReviewService(f.Store, f.Time).ReviewAsync(created.Question!.Key, token);
            review.Outcome.Should().Be(HostInteractionOutcome.Presented);
            var answer = text ? new QuestionAnswer([], "Exact original-user answer") : new QuestionAnswer(["option-1"]);
            var submitted = await f.Questions.SubmitAsync(created.Question.Key, answer, channel, token);
            return new(created.Question, submitted);
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);

        var result = await broker.AskAsync(new("Which format?", text ? [] : ["Compact", "Detailed"]), Token);
        result.Question.AnswerChannel.Should().Be(channel);
        result.Question.Status.Should().Be(QuestionStatus.Answered);
        result.Question.Proposal.Should().BeNull();
        result.Question.Draft!.Text.Should().Be(text ? "Exact original-user answer" : null);
        f.Store.Commits.Last().Audit.ActionId.Should().Be("question.continue");
        f.Store.LastActivity!.Request.Should().Be(f.Request);
        await broker.Invoking(value => value.AskAsync(new("Again?", ["One", "Two"]), Token))
            .Should().ThrowAsync<InvalidOperationException>();
        presentations.Should().Be(1);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("foreign-request")]
    [InlineData("foreign-id")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("spec")]
    [InlineData("deadline")]
    [InlineData("proposal")]
    [InlineData("channel")]
    [InlineData("answer")]
    [InlineData("canonical-removed")]
    [InlineData("canonical-revised")]
    [InlineData("canonical-answer")]
    [InlineData("admission")]
    [InlineData("session")]
    [InlineData("terminal-task")]
    [InlineData("audit")]
    [InlineData("cancel-token")]
    public async Task ChangedOrUnsubmittedAnswersFailClosedWithoutContinuation(string fault)
    {
        using var f = new InteractionFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var live = true;
        var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
        {
            var created = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            var completed = fault switch
            {
                "draft" => await f.Questions.DraftAsync(created.Key, new(["option-0"]), RequestOrigin.LocalUi, token),
                "cancel" => await f.Questions.CancelAsync(created.Key, token),
                _ => await f.Questions.SubmitAsync(created.Key, new(["option-0"]), RequestOrigin.LocalUi, token),
            };
            var answered = completed.Question!;
            answered = fault switch
            {
                "foreign-request" => answered with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), answered.Key.QuestionId, answered.Key.Revision) },
                "foreign-id" => answered with { Key = new(answered.Key.Request, new(Guid.NewGuid()), answered.Key.Revision) },
                "generation" => answered with { SessionGeneration = new(2) },
                "revision" => answered with { Key = created.Key },
                "spec" => answered with { Spec = InteractionFixture.Choice() },
                "deadline" => answered with { ExpiresAt = expiry.AddSeconds(1) },
                "proposal" => answered with { Proposal = f.Store.Snapshot.Proposal },
                "channel" => answered with { AnswerChannel = RequestOrigin.HostSystem },
                "answer" => answered with { Draft = new(["option-0", "option-1"]) },
                _ => answered,
            };
            if (fault is "expired") { f.Time.Now = expiry; }
            if (fault is "admission") { live = false; }
            if (fault is "audit") { f.Store.FailAudit = true; }
            if (fault is "cancel-token") { cancellation.Cancel(); }
            f.Store.Change(snapshot => fault switch
            {
                "canonical-removed" => snapshot with { Questions = [] },
                "canonical-revised" => snapshot with { Questions = [answered with { Key = answered.Key.Next() }] },
                "canonical-answer" => snapshot with { Questions = [answered with { Draft = new(["option-1"]) }] },
                "session" => snapshot with { Session = snapshot.Session with { Generation = new(2) } },
                "terminal-task" => snapshot with { Intent = new(snapshot.Intent.Request, snapshot.Intent.Revision, HostTaskState.Cancelled) },
                _ => snapshot,
            });
            return new(created, completed with { Question = answered });
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), () => live, f.Time);

        await broker.Invoking(value => value.AskAsync(new("Choose", ["One", "Two"]), cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Store.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue"
            && commit.Decision.Outcome == HostInteractionOutcome.Answered);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("expired")]
    [InlineData("closed")]
    public async Task MissingOriginalLiveHostContextNeverPresentsOrAuditsAnAnswer(string fault)
    {
        using var f = new InteractionFixture();
        using var root = fault is "missing" ? null : HostActivity.BeginRoot(
            fault is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request,
            HostActivityLayer.Application, HostOperation.Request);
        if (fault is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        if (fault is "stopped")
        {
            root!.Activity!.Stop();
            System.Diagnostics.Activity.Current = root.Activity;
            root.Activity.IsStopped.Should().BeTrue();
            HostActivity.Current.Should().BeNull("the host must reject a framework-retired activity before question admission");
        }
        var presentations = 0;
        var broker = new ConversationQuestionContinuation(f.Store, (_, _, _, _, _, _) =>
        {
            presentations++;
            throw new InvalidOperationException("Must not present");
        }, f.Request, new(1), f.Time.Now.AddSeconds(fault is "expired" ? 0 : 15),
            () => fault is not "closed", f.Time);
        await broker.Invoking(value => value.AskAsync(new("Choose", ["One", "Two"]), Token))
            .Should().ThrowAsync<InvalidOperationException>();
        presentations.Should().Be(0);
        f.Store.Commits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("generation")]
    [InlineData("expiry")]
    [InlineData("proposal")]
    [InlineData("channel")]
    [InlineData("text")]
    [InlineData("kind")]
    [InlineData("purpose")]
    [InlineData("source")]
    [InlineData("text-limit")]
    [InlineData("options")]
    [InlineData("missing-answer")]
    [InlineData("answer-text")]
    [InlineData("answer-choices")]
    public async Task CanonicalAnsweredRecordMustMatchEveryValidatedReceiptFieldBeforeResume(string fault)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
        {
            var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            var answer = spec.Kind == QuestionKind.Text ? new QuestionAnswer([], "Fresh text") : new QuestionAnswer(["option-0"]);
            var completed = await f.Questions.SubmitAsync(initial.Key, answer, RequestOrigin.LocalUi, token);
            var answered = completed.Question!;
            var changedSpec = fault switch
            {
                "text" => new QuestionSpec("Different prompt", spec.Kind, spec.Options, purpose: spec.Purpose, sourceId: spec.SourceId),
                "kind" => new(spec.Text, QuestionKind.MultipleChoice, spec.Options, purpose: spec.Purpose, sourceId: spec.SourceId),
                "purpose" => new(spec.Text, spec.Kind, spec.Options, purpose: "other", sourceId: spec.SourceId),
                "source" => new(spec.Text, spec.Kind, spec.Options, purpose: spec.Purpose, sourceId: "other"),
                "text-limit" => new(spec.Text, QuestionKind.Text, [], maximumTextLength: 499, purpose: spec.Purpose, sourceId: spec.SourceId),
                "options" => new(spec.Text, spec.Kind, [new("option-0", "Changed"), spec.Options[1]], purpose: spec.Purpose, sourceId: spec.SourceId),
                _ => spec,
            };
            var canonical = answered with { Spec = changedSpec };
            canonical = fault switch
            {
                "status" => canonical with { Status = QuestionStatus.Pending },
                "generation" => canonical with { SessionGeneration = new(2) },
                "expiry" => canonical with { ExpiresAt = expiry.AddSeconds(1) },
                "proposal" => canonical with { Proposal = f.Store.Snapshot.Proposal },
                "channel" => canonical with { AnswerChannel = RequestOrigin.ActivatedVoice },
                "missing-answer" => canonical with { Draft = null },
                "answer-text" => canonical with { Draft = new(["option-0"], "Foreign text") },
                "answer-choices" => canonical with { Draft = new(["option-1"]) },
                _ => canonical,
            };
            f.Store.Change(snapshot => snapshot with { Questions = [canonical] });
            return new(initial, completed);
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
        var question = new LocalModelQuestion("Choose format", ["Compact", "Detailed"]);
        await broker.Invoking(value => value.AskAsync(fault is "text-limit" ? new("Describe format", []) : question, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Store.Commits.Last().Decision.Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Theory]
    [InlineData("one-option")]
    [InlineData("too-many")]
    [InlineData("ambiguous")]
    [InlineData("prompt")]
    [InlineData("label")]
    [InlineData("credential")]
    [InlineData("control")]
    public async Task UnsupportedQuestionShapeIsRejectedBeforePresentation(string fault)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var question = fault switch
        {
            "one-option" => new LocalModelQuestion("Choose", ["One"]),
            "too-many" => new("Choose", ["One", "Two", "Three", "Four", "Five"]),
            "ambiguous" => new("Choose", ["Same", "Same"]),
            "prompt" => new(new string('x', 501), ["One", "Two"]),
            "label" => new("Choose", [new string('x', 81), "Two"]),
            "credential" => new("api_key=synthetic-only-value", ["One", "Two"]),
            _ => new("Unsupported\0", ["One", "Two"]),
        };
        var broker = new ConversationQuestionContinuation(f.Store,
            (_, _, _, _, _, _) => throw new InvalidOperationException("Must not present"),
            f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
        var failure = await broker.Invoking(value => value.AskAsync(question, Token)).Should().ThrowAsync<Exception>();
        failure.Which.GetType().Should().Be(fault is "credential"
            ? typeof(Kora.Core.Conversation.ConversationCredentialException) : typeof(InvalidDataException));
        f.Store.Commits.Should().BeEmpty();
    }
}
