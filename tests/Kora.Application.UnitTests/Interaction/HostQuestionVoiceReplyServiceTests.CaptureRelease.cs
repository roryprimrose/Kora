using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class HostQuestionVoiceReplyServiceTests
{
    [Theory]
    [InlineData("choice")]
    [InlineData("multiple")]
    [InlineData("text")]
    [InlineData("field")]
    public async Task Unconfirmed_capture_release_cannot_submit_an_answer_or_edit_a_field(string kind)
    {
        var f = new QuestionReplyFixture();
        var (spec, transcript, field) = CaptureReleaseQuestion(kind);
        var question = await f.Host.QuestionAsync(spec);
        QuestionFieldAnswer? edited = null;
        if (field is null) { await f.OpenAsync(question); }
        else
        {
            await f.Replies.OpenFieldAsync(question, field, () => f.Visible, value => edited = value,
                TestContext.Current.CancellationToken);
        }
        var commits = f.Host.Store.Commits.Count;
        var generation = f.Capture.Generation;
        f.Capture.NonQuiescent = true;
        f.Capture.Transcript(transcript);
        var quiescence = () => f.Replies.Quiescence;
        await quiescence.Should().ThrowAsync<InvalidOperationException>();

        f.Accepted.Should().BeNull();
        edited.Should().BeNull();
        f.Host.Store.Snapshot.Questions.Should().ContainSingle().Which.Should().Be(question);
        f.Host.Store.Commits.Should().HaveCount(commits);
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Capture.IsCaptureQuiescent.Should().BeFalse();
        f.Replies.IsActive.Should().BeFalse();
        f.Replies.Status.Should().Contain("cleanup unconfirmed");

        f.Capture.Transcript(transcript, generation);
        f.Capture.NonQuiescent = false;
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(1);
        f.Accepted.Should().BeNull();
        edited.Should().BeNull();
        f.Host.Store.Commits.Should().HaveCount(commits);
        QuestionAnswer recovery = field is not null
            ? new([], fields: [new(field, "Typed recovery")])
            : spec.Kind == QuestionKind.Text ? new([], "Typed recovery") : new(["b"]);
        var native = await f.Host.RunAsync(() => f.Host.Questions.SubmitAsync(question.Key, recovery,
            RequestOrigin.LocalUi, TestContext.Current.CancellationToken));
        native.Outcome.Should().Be(HostInteractionOutcome.Answered);
        native.Question!.Key.Should().Be(question.Key.Next());
        native.Question.AnswerChannel.Should().Be(RequestOrigin.LocalUi);
        native.Question.Draft.Should().BeEquivalentTo(recovery);
        f.Accepted.Should().BeNull();
        edited.Should().BeNull();
        var dispose = async () => await f.Replies.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
        await f.Capture.DisposeAsync();
        f.Host.Dispose();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("timeout")]
    [InlineData("recognizer")]
    [InlineData("privacy")]
    public async Task Retirement_cannot_claim_quiescence_or_reopen_after_unconfirmed_native_release(string reason)
    {
        var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var commits = f.Host.Store.Commits.Count;
        f.Capture.NonQuiescent = true;
        switch (reason)
        {
            case "cancel": f.Replies.Retire("Cancel reply"); break;
            case "timeout": f.Time.Advance(TimeSpan.FromSeconds(15)); break;
            case "recognizer": f.Capture.Fail(); break;
            case "privacy": f.Admission = null; f.Replies.Revalidate(); break;
        }
        var quiescence = () => f.Replies.Quiescence;
        await quiescence.Should().ThrowAsync<InvalidOperationException>();
        f.Replies.Status.Should().Contain("cleanup unconfirmed");
        f.Replies.IsActive.Should().BeFalse();
        f.Host.Store.Snapshot.Questions.Should().ContainSingle().Which.Should().Be(question);
        f.Host.Store.Commits.Should().HaveCount(commits);
        f.Accepted.Should().BeNull();

        f.Capture.NonQuiescent = false;
        f.Admission = new(new("mic", "Synthetic"), 1, 1, 1);
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(1);
        var dispose = async () => await f.Replies.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
        await f.Capture.DisposeAsync();
        f.Host.Dispose();
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("multiple")]
    [InlineData("text")]
    [InlineData("field")]
    public async Task Confirmed_release_admits_only_the_original_answer_or_draft_only_field_after_join(string kind)
    {
        await using var f = new QuestionReplyFixture();
        var (spec, transcript, field) = CaptureReleaseQuestion(kind);
        var question = await f.Host.QuestionAsync(spec);
        QuestionFieldAnswer? edited = null;
        if (field is null) { await f.OpenAsync(question); }
        else
        {
            await f.Replies.OpenFieldAsync(question, field, () => f.Visible, value => edited = value,
                TestContext.Current.CancellationToken);
        }
        var commits = f.Host.Store.Commits.Count;
        f.Capture.NonQuiescent = true;
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript(transcript);
        await f.Capture.StopStarted.Task;
        f.Accepted.Should().BeNull();
        edited.Should().BeNull();
        f.Host.Store.Commits.Should().HaveCount(commits);
        f.Replies.Quiescence.IsCompleted.Should().BeFalse();

        f.Capture.NonQuiescent = false;
        f.Capture.Closing.SetResult();
        await f.Replies.Quiescence;
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Replies.IsActive.Should().BeFalse();
        if (field is null)
        {
            f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
            f.Accepted.Question!.Key.Should().Be(question.Key.Next());
            f.Accepted.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
            f.Host.Store.Commits.Count(commit => commit.Decision.Outcome == HostInteractionOutcome.Answered
                && string.Equals(commit.Audit.ActionId, "question.voice-submit", StringComparison.Ordinal)).Should().Be(1);
            edited.Should().BeNull();
        }
        else
        {
            edited.Should().Be(new QuestionFieldAnswer(field, transcript));
            f.Accepted.Should().BeNull();
            f.Host.Store.Snapshot.Questions.Should().ContainSingle().Which.Should().Be(question);
            f.Host.Store.Commits.Should().HaveCount(commits);
        }
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
    }

    private static (QuestionSpec Spec, string Transcript, string? Field) CaptureReleaseQuestion(string kind) => kind switch
    {
        "choice" => (InteractionFixture.Choice(), "First", null),
        "multiple" => (new("Choose", QuestionKind.MultipleChoice, [new("a", "First"), new("b", "Second")],
            minimum: 1, maximum: 2), "First and Second", null),
        "text" => (new("Answer", QuestionKind.Text, [], maximumTextLength: 40), "Readers", null),
        "field" => (new("One field", QuestionKind.Form, [],
            fields: [new("field", "Audience", QuestionKind.Text, [], 40, 100)]), "Readers", "field"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
