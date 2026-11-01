using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed class HostQuestionFormTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static LocalModelQuestion Form() => ModelToolProtocol.ParseAskUser(
        """{"prompt":"Describe the answer","fields":[{"label":"Audience","type":"text","maximumLength":40,"maximumUtf8Bytes":100},{"label":"Style","type":"singleChoice","options":["Compact","Detailed"]}]}"""u8,
        "ask_1", formsAllowed: true).Question;

    private static QuestionAnswer Answer() => new([], fields: [new("field-1", Choice: "option-1"), new("field-0", "Readers")]);

    [Fact]
    public async Task WholeFormIsVolatileUntilOneExplicitCanonicalCommitAndExactContinuation()
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
        {
            var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            var denied = await f.Questions.DraftAsync(initial.Key, Answer(), RequestOrigin.ActivatedVoice, token);
            denied.Reason.Should().Be("form-drafts-are-volatile");
            f.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
            var submitted = await f.Questions.SubmitAsync(initial.Key, Answer(), RequestOrigin.LocalUi, token);
            submitted.Outcome.Should().Be(HostInteractionOutcome.Answered);
            var replay = await f.Questions.SubmitAsync(initial.Key, Answer(), RequestOrigin.ActivatedVoice, token);
            replay.Outcome.Should().Be(HostInteractionOutcome.Conflict);
            return new(initial, submitted);
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), static () => true, f.Time);
        var result = await broker.AskAsync(Form(), Token);
        result.Question.Draft!.Fields.Select(field => field.FieldId).Should().Equal("field-0", "field-1");
        f.Store.Commits.Count(commit => commit.Decision.Outcome == HostInteractionOutcome.Answered
            && string.Equals(commit.Audit.ActionId, "question.submit", StringComparison.Ordinal)).Should().Be(1);
        f.Store.Snapshot.Grants.Should().BeEmpty();
        var replayContinuation = () => broker.AskAsync(Form(), Token);
        await replayContinuation.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("replace")]
    [InlineData("foreign")]
    [InlineData("field-replacement")]
    [InlineData("answer-replacement")]
    public async Task CancelExpiryChangedAuthorityOrCanonicalFormCannotContinue(string fault)
    {
        using var f = new InteractionFixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var live = true;
        var broker = new ConversationQuestionContinuation(f.Store, async (request, _, spec, expiry, _, token) =>
        {
            var initial = (await f.Questions.CreateAsync(request, spec, expiry, token)).Question!;
            if (fault is "cancel") { return new(initial, await f.Questions.CancelAsync(initial.Key, token)); }
            if (fault is "expiry") { f.Time.Now = expiry; }
            if (fault is "generation") { f.Store.Change(snapshot => snapshot with { Session = snapshot.Session with { Generation = new(2) } }); }
            if (fault is "replace") { await f.Questions.ReviseAsync(initial.Key, spec, expiry, token); }
            if (fault is "foreign")
            {
                var foreign = new HostQuestionKey(request, new(Guid.NewGuid()), initial.Key.Revision);
                return new(initial, await f.Questions.SubmitAsync(foreign, Answer(), RequestOrigin.LocalUi, token));
            }
            var submitted = await f.Questions.SubmitAsync(initial.Key, Answer(), RequestOrigin.LocalUi, token);
            if (fault is "privacy") { live = false; }
            if (fault is "field-replacement")
            {
                var changed = new QuestionSpec(spec.Text, QuestionKind.Form, [], purpose: spec.Purpose, sourceId: spec.SourceId,
                    fields: [new("field-0", "Different audience", QuestionKind.Text, [], 40, 100), spec.Fields[1]]);
                f.Store.Change(snapshot => snapshot with { Questions = [submitted.Question! with { Spec = changed }] });
            }
            if (fault is "answer-replacement")
            {
                f.Store.Change(snapshot => snapshot with
                {
                    Questions = [submitted.Question! with { Draft = new([], fields: [new("field-0", "Other"), new("field-1", Choice: "option-0")]) }],
                });
            }
            return new(initial, submitted);
        }, f.Request, new(1), f.Time.Now.AddSeconds(15), () => live, f.Time);
        await broker.Invoking(value => value.AskAsync(Form(), Token)).Should().ThrowAsync<Exception>();
        f.Store.Commits.Should().NotContain(commit => commit.Audit.ActionId == "question.continue"
            && commit.Decision.Outcome == HostInteractionOutcome.Answered);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VoiceFieldEntryEditsOnlyVolatileDataAndCannotSubmitOrOverwriteChangedDraft(bool changed)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync(LocalModelQuestionPolicy.CreateSpec(Form()));
        QuestionFieldAnswer? edited = null;
        var revision = 1;
        await f.Replies.OpenFieldAsync(question, "field-0", () => f.Visible && revision == 1,
            value => edited = value, Token);
        f.Spoken.Should().Contain("maximum 40 characters").And.Contain("edits this field only");
        if (changed) { revision++; }
        f.Capture.Transcript("Native readers");
        if (changed) { f.Replies.Revalidate(); }
        await f.Replies.Quiescence;
        edited.Should().Be(changed ? null : new QuestionFieldAnswer("field-0", "Native readers"));
        f.Host.Store.Snapshot.Questions.Single().Key.Should().Be(question.Key);
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Accepted.Should().BeNull();
        f.Capture.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("not-form")]
    [InlineData("approval")]
    [InlineData("foreign-field")]
    [InlineData("changed-schema")]
    public async Task VoiceFormEntryRequiresAnExactOrdinaryFieldAndCannotUseGenericQuestionSubmission(string fault)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync(fault is "not-form" ? InteractionFixture.Choice()
            : LocalModelQuestionPolicy.CreateSpec(Form()));
        if (fault is "generic")
        {
            await f.OpenAsync(question);
        }
        else
        {
            if (fault is "approval") { question = question with { Proposal = f.Host.Store.Snapshot.Proposal }; }
            if (fault is "changed-schema")
            {
                var changed = new QuestionSpec(question.Spec.Text, QuestionKind.Form, [],
                    fields: [new("field-0", "Changed", QuestionKind.Text, [], 40, 100)]);
                f.Host.Store.Change(snapshot => snapshot with { Questions = [question with { Spec = changed }] });
            }
            var open = () => f.Replies.OpenFieldAsync(question, fault is "foreign-field" ? "foreign" : "field-0",
                () => f.Visible, _ => throw new InvalidOperationException("No edit permitted"), Token);
            if (fault is "changed-schema") { await open(); }
            else { await open.Should().ThrowAsync<InvalidOperationException>(); }
        }
        f.Capture.Opens.Should().Be(0);
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("bad-choice")]
    [InlineData("bytes")]
    [InlineData("late")]
    public async Task SpokenFieldChoicesAndFullByteBoundsRemainDraftOnly(string scenario)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync(LocalModelQuestionPolicy.CreateSpec(Form()));
        QuestionFieldAnswer? value = null;
        await f.Replies.OpenFieldAsync(question, scenario is "choice" or "bad-choice" ? "field-1" : "field-0",
            () => f.Visible, answer => value = answer, Token);
        if (scenario is "late") { f.Capture.Closing = new(); }
        f.Capture.Transcript(scenario is "choice" ? "option two" : scenario is "bad-choice" ? "unknown"
            : scenario is "bytes" ? new('\u754c', 34) : "Readers");
        if (scenario is "late")
        {
            await f.Capture.StopStarted.Task;
            f.Visible = false;
            f.Capture.Closing!.SetResult();
        }
        await f.Replies.Quiescence;
        value.Should().Be(scenario is "choice" ? new QuestionFieldAnswer("field-1", Choice: "option-1") : null);
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.Accepted.Should().BeNull();
    }
}
