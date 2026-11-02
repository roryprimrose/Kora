using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.Skills;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Skills;

public sealed class OwnedSkillInputQuestionsTests
{
    [Theory]
    [InlineData("foreign-request")]
    [InlineData("expired")]
    [InlineData("retired")]
    [InlineData("policy")]
    [InlineData("no-question")]
    [InlineData("null-question")]
    [InlineData("null-draft")]
    [InlineData("schema")]
    [InlineData("request")]
    [InlineData("expiry")]
    [InlineData("generation")]
    [InlineData("proposal")]
    [InlineData("question-id")]
    [InlineData("revision")]
    [InlineData("channel")]
    [InlineData("shape")]
    [InlineData("audit")]
    public async Task VolatileQuestionStoreRefusesAlteredBindingsBeforeAnswerPublication(string fault)
    {
        using var f = new InteractionFixture();
        var spec = new QuestionSpec("Literal field", QuestionKind.Text, [], maximumTextLength: 512,
            purpose: OwnedSkillInputQuestions.Purpose, sourceId: "owned-skill-input");
        var audit = new QuestionAudit();
        using var owner = new OwnedSkillInputQuestions(f.Request, f.Store.Snapshot.Session, f.Store.Snapshot.Intent,
            spec, f.Time.Now.AddMinutes(2), f.Time, _ => Task.CompletedTask,
            (operation, _) => ValueTask.FromResult(operation()), audit);
        var questions = new HostQuestionService(owner, f.Time);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(f.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var initial = (await questions.CreateAsync(f.Request, spec, owner.ExpiresAt, TestContext.Current.CancellationToken)).Question!;
        if (fault is "retired") { owner.Dispose(); }
        if (fault is "expired") { f.Time.Now = owner.ExpiresAt; }
        if (fault is "audit") { audit.Fail = true; }
        var request = fault is "foreign-request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request;
        await owner.Invoking(async value => await value.TransactAsync(request, before =>
        {
            var question = initial with { Key = initial.Key.Next(), Draft = new([], "value"),
                AnswerChannel = RequestOrigin.LocalUi, Status = QuestionStatus.Answered };
            if (fault is "schema") { question = question with { Spec = new("Changed", QuestionKind.Text, [], maximumTextLength: 512) }; }
            if (fault is "request") { question = question with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), initial.Key.QuestionId, initial.Key.Revision) }; }
            if (fault is "null-draft") { question = question with { Draft = null }; }
            if (fault is "expiry") { question = question with { ExpiresAt = question.ExpiresAt.AddTicks(1) }; }
            if (fault is "generation") { question = question with { SessionGeneration = new(2) }; }
            if (fault is "proposal") { question = question with { Proposal = f.Store.Snapshot.Proposal }; }
            if (fault is "question-id") { question = question with { Key = new(f.Request, new(Guid.NewGuid()), initial.Key.Revision) }; }
            if (fault is "revision") { question = question with { Key = initial.Key.Next().Next() }; }
            if (fault is "channel") { question = question with { AnswerChannel = RequestOrigin.ActivatedVoice }; }
            if (fault is "shape") { question = question with { Draft = new(["approve"]) }; }
            var next = before with { Questions = fault is "no-question" ? [] : fault is "null-question" ? [null!] : [question] };
            if (fault is "policy") { next = next with { Policy = next.Policy with { IsUnlocked = false } }; }
            return new(next, new(HostInteractionOutcome.Answered, "synthetic", question),
                new(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval, "question.submit",
                    SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "literal-input"));
        }, TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
        owner.Invoking(value => value.Accept(new(HostInteractionOutcome.Answered, "foreign", initial)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AnswerMustBeExactCommittedReceiptAndCanOnlyBeConsumedOnce()
    {
        using var f = new InteractionFixture();
        var spec = new QuestionSpec("Literal field", QuestionKind.Text, [], maximumTextLength: 512,
            purpose: OwnedSkillInputQuestions.Purpose, sourceId: "owned-skill-input");
        using var owner = new OwnedSkillInputQuestions(f.Request, f.Store.Snapshot.Session, f.Store.Snapshot.Intent,
            spec, f.Time.Now.AddMinutes(2), f.Time, _ => Task.CompletedTask,
            (operation, _) => ValueTask.FromResult(operation()), new QuestionAudit());
        var questions = new HostQuestionService(owner, f.Time);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(f.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var initial = (await questions.CreateAsync(f.Request, spec, owner.ExpiresAt, TestContext.Current.CancellationToken)).Question!;
        var answer = await questions.SubmitAsync(initial.Key, new([], "exact value"), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        owner.Invoking(value => value.Accept(answer with { Question = answer.Question! with { Draft = new([], "foreign") } }))
            .Should().Throw<InvalidOperationException>();
        owner.Accept(answer).Should().Be("exact value");
        owner.Invoking(value => value.Accept(answer)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    [InlineData("  unsubmitted ${input:topic} e\u0301  ")]
    public async Task CanonicalDraftRemainsPendingAndCannotBindBeforeSeparateNonblankSubmit(string value)
    {
        using var f = new InteractionFixture();
        var spec = new QuestionSpec("Literal field", QuestionKind.Text, [], maximumTextLength: 512,
            purpose: OwnedSkillInputQuestions.Purpose, sourceId: "owned-skill-input");
        using var owner = new OwnedSkillInputQuestions(f.Request, f.Store.Snapshot.Session, f.Store.Snapshot.Intent,
            spec, f.Time.Now.AddMinutes(2), f.Time, _ => Task.CompletedTask,
            (operation, _) => ValueTask.FromResult(operation()), new QuestionAudit());
        var questions = new HostQuestionService(owner, f.Time);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(f.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var initial = (await questions.CreateAsync(f.Request, spec, owner.ExpiresAt, TestContext.Current.CancellationToken)).Question!;
        var drafted = await questions.DraftAsync(initial.Key, new([], value), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        drafted.Outcome.Should().Be(HostInteractionOutcome.Drafted);
        var pending = drafted.Question!;
        pending.Key.Should().Be(initial.Key.Next());
        pending.Spec.Should().BeSameAs(spec);
        pending.Status.Should().Be(QuestionStatus.Pending);
        pending.Draft!.Text.Should().Be(value);
        pending.ExpiresAt.Should().Be(initial.ExpiresAt);
        pending.SessionGeneration.Should().Be(initial.SessionGeneration);
        owner.Invoking(collection => collection.Accept(drafted)).Should().Throw<InvalidOperationException>();
        owner.Invoking(collection => collection.Accept(drafted with { Outcome = HostInteractionOutcome.Answered }))
            .Should().Throw<InvalidOperationException>("a draft cannot be promoted by relabelling its receipt");
        var refused = await questions.SubmitAsync(pending.Key, new([], ""), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        refused.Outcome.Should().Be(HostInteractionOutcome.Denied);
        owner.Invoking(collection => collection.Accept(refused)).Should().Throw<InvalidOperationException>();
        var submitted = await questions.SubmitAsync(pending.Key, new([], "  exact submitted e\u0301  "), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        submitted.Outcome.Should().Be(HostInteractionOutcome.Answered);
        submitted.Question!.Key.Should().Be(pending.Key.Next());
        submitted.Question.Status.Should().Be(QuestionStatus.Answered);
        owner.Accept(submitted).Should().Be("  exact submitted e\u0301  ");
        owner.Invoking(collection => collection.Accept(submitted)).Should().Throw<InvalidOperationException>();
        f.Store.Snapshot.Questions.Should().BeEmpty("literal question drafts remain volatile");
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("unicode")]
    [InlineData("secret")]
    [InlineData("control")]
    [InlineData("choice")]
    [InlineData("channel")]
    public async Task DraftingStillRejectsInvalidContentShapeAndOriginalChannelBeforePublication(string fault)
    {
        using var f = new InteractionFixture();
        var spec = new QuestionSpec("Literal field", QuestionKind.Text, [], maximumTextLength: 512,
            purpose: OwnedSkillInputQuestions.Purpose, sourceId: "owned-skill-input");
        using var owner = new OwnedSkillInputQuestions(f.Request, f.Store.Snapshot.Session, f.Store.Snapshot.Intent,
            spec, f.Time.Now.AddMinutes(2), f.Time, _ => Task.CompletedTask,
            (operation, _) => ValueTask.FromResult(operation()), new QuestionAudit());
        var questions = new HostQuestionService(owner, f.Time);
        using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(f.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var initial = (await questions.CreateAsync(f.Request, spec, owner.ExpiresAt, TestContext.Current.CancellationToken)).Question!;
        var value = fault switch
        {
            "oversized" => new string('a', 513),
            "unicode" => new string('\ud800', 1),
            "secret" => "api_key=synthetic-only-value",
            "control" => "\0",
            _ => "valid",
        };
        HostInteractionDecision? draft = null;
        var exception = await Record.ExceptionAsync(async () =>
            draft = await questions.DraftAsync(initial.Key, fault is "choice" ? new(["approve"]) : new([], value),
                fault is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, TestContext.Current.CancellationToken));
        if (fault is "oversized" or "choice") { draft!.Outcome.Should().Be(HostInteractionOutcome.Denied); }
        else { exception.Should().NotBeNull(); }
        owner.Invoking(collection => collection.Accept(new(HostInteractionOutcome.Answered, "synthetic", initial)))
            .Should().Throw<InvalidOperationException>();
        var answer = await questions.SubmitAsync(initial.Key, new([], "accepted"), RequestOrigin.LocalUi,
            TestContext.Current.CancellationToken);
        owner.Accept(answer).Should().Be("accepted", "refused drafts never change the original question revision");
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    private sealed class QuestionAudit : ISecurityAuditLog
    {
        internal bool Fail { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Fail) { throw new IOException("Synthetic audit failure"); }
            auditEvent.ActionId.Should().StartWith("question.");
        }
    }
}
