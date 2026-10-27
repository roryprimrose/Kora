using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class HostQuestionVoiceReplyServiceTests
{
    [Theory]
    [InlineData("First", "manual")]
    [InlineData("Nova First", "manual")]
    [InlineData("Nova, 1", "playback")]
    [InlineData("Nova option 1", "conversational")]
    public async Task Named_or_bare_choice_completes_same_one_shot_question_with_original_channel(string transcript, string route)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1) };
        f.Admission = f.Admission! with { ActivationName = "Nova" };
        var question = await f.Host.QuestionAsync();
        if (route is "conversational") { await f.OpenConversationalAsync(question); }
        else { await f.OpenAsync(question, afterPlayback: route is "playback"); }
        f.Capture.Speech();
        f.Capture.Transcript(transcript);
        await f.Replies.Quiescence;
        var answered = f.Host.Store.Snapshot.Questions.Single();
        f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
        answered.Key.Should().Be(question.Key.Next());
        answered.SessionGeneration.Should().Be(question.SessionGeneration);
        answered.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        answered.Draft!.Choices.Should().Equal("a");
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Transcript("Nova Second");
        f.Time.Advance(TimeSpan.FromMinutes(2));
        f.Capture.Opens.Should().Be(1);
        f.SpeechCalls.Should().Be(route is "manual" ? 0 : 1);
        f.Host.Store.Snapshot.Questions.Single().Should().Be(answered);
    }

    [Theory]
    [InlineData("Kora First", null)]
    [InlineData("Atlas First", "Nova")]
    [InlineData("Nova Nova First", "Nova")]
    [InlineData("Nova", "Nova")]
    [InlineData("Nov First", "Nova")]
    public async Task Absent_other_partial_repeated_or_empty_names_never_submit_or_fall_back(string transcript, string? name)
    {
        await using var f = new QuestionReplyFixture();
        f.Admission = f.Admission! with { ActivationName = name };
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Capture.Transcript(transcript);
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
        f.Replies.Status.Should().Contain("did not match");
        f.Host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Opens.Should().Be(1);
    }

    [Fact]
    public async Task Captured_name_never_changes_free_text_bytes_or_bare_literal_label_meaning()
    {
        await using var f = new QuestionReplyFixture();
        f.Admission = f.Admission! with { ActivationName = "Nova" };
        const string text = "  Nova,\tFirst\r\n";
        var question = await f.Host.QuestionAsync(new("Explain", QuestionKind.Text, [], maximumTextLength: text.Length));
        await f.OpenAsync(question);
        f.Capture.Transcript(text);
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Text.Should().Be(text);
        question = await f.Host.QuestionAsync(new("Choose", QuestionKind.SingleChoice,
            [new("bare", "Nova First"), new("other", "First")]));
        await f.OpenAsync(question);
        f.Capture.Transcript("Nova First");
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Choices.Should().Equal("bare");
    }

    [Theory]
    [InlineData("playback")]
    [InlineData("capture")]
    [InlineData("dispatch")]
    [InlineData("commit")]
    public async Task Name_replacement_retires_original_attempt_and_joins_release_despite_late_callbacks(string phase)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1) };
        f.Admission = f.Admission! with { ActivationName = "Nova" };
        var question = await f.Host.QuestionAsync();
        if (phase is "playback") { f.Playback = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var opening = f.OpenConversationalAsync(question);
        if (phase is not "playback") { await opening; }
        var generation = f.Capture.Generation;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (phase is not "commit") { f.Capture.Closing = release; }
        void ChangeName()
        {
            f.Capture.Closing = release;
            f.Admission = f.Admission! with { ActivationName = "Atlas" };
            f.Replies.Revalidate();
        }
        try
        {
            if (phase is "dispatch")
            {
                f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                f.Capture.Transcript("Nova First");
                ChangeName();
                f.Dispatcher.Hold.SetResult();
            }
            else if (phase is "commit")
            {
                f.Host.Store.BeforeTransition = ChangeName;
                f.Capture.Transcript("Nova First");
            }
            else { ChangeName(); }
            f.Replies.IsActive.Should().BeFalse();
            f.Capture.IsListening.Should().BeFalse();
            (f.Capture.Admitted?.Invoke()).Should().NotBe(true);
            f.Replies.Quiescence.IsCompleted.Should().BeFalse();
            f.Admission = f.Admission! with { ActivationName = "Nova" };
            f.Capture.Transcript("Nova First", generation);
            f.Capture.Speech(generation);
            f.Capture.Complete(generation);
            release.SetResult();
            f.Playback?.TrySetResult();
            await opening;
            await f.Replies.Quiescence;
            f.Accepted.Should().BeNull();
            f.Host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
            f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
            f.Capture.Opens.Should().Be(phase is "playback" ? 0 : 1);
            f.Capture.IsCaptureQuiescent.Should().BeTrue();
        }
        finally
        {
            release.TrySetResult();
            f.Dispatcher.Hold?.TrySetResult();
            f.Playback?.TrySetResult();
        }
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("device")]
    [InlineData("recovery")]
    [InlineData("microphone")]
    [InlineData("window")]
    [InlineData("question")]
    [InlineData("expiry")]
    public async Task Named_answers_retain_all_original_admission_and_target_fences(string boundary)
    {
        await using var f = new QuestionReplyFixture();
        f.Admission = f.Admission! with { ActivationName = "Nova" };
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var generation = f.Capture.Generation;
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        switch (boundary)
        {
            case "privacy": f.Admission = null; break;
            case "call": f.Admission = f.Admission! with { CallRevision = 2 }; break;
            case "device": f.Admission = f.Admission! with { TopologyRevision = 2 }; break;
            case "recovery": f.Admission = f.Admission! with { RecoveryRevision = 2 }; break;
            case "microphone": f.Admission = f.Admission! with { Microphone = new("other", "Synthetic") }; break;
            case "window": f.Visible = false; break;
            case "question": await f.Host.QuestionAsync(); break;
            case "expiry": f.Time.Advance(question.ExpiresAt - f.Time.Now); break;
        }
        f.Replies.Revalidate();
        f.Capture.Transcript("Nova First", generation);
        f.Capture.Speech(generation);
        f.Capture.Complete(generation);
        f.Replies.IsActive.Should().BeFalse();
        f.Replies.Quiescence.IsCompleted.Should().BeFalse();
        f.Capture.Closing.SetResult();
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
        f.Host.Store.Snapshot.Questions.Should().OnlyContain(record => record.Draft == null);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("egress-consent")]
    [InlineData("consent")]
    [InlineData("native-only")]
    public async Task Captured_name_never_enables_excluded_conversational_questions(string purpose)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1) };
        f.Admission = f.Admission! with { ActivationName = "Nova" };
        var question = purpose is "approval"
            ? (await f.Host.RunAsync(() => f.Host.Authorization.PresentAsync(f.Host.Request, CancellationToken.None))).Question!
            : await f.Host.QuestionAsync(new("Native only", QuestionKind.SingleChoice, [new("yes", "Yes")], purpose: purpose));
        if (purpose is "approval")
        {
            QuestionVoiceAnswer.Parse(question, "Nova option 1", "Nova").Should().BeNull();
            await f.OpenAsync(question);
        }
        await f.OpenConversationalAsync(question);
        f.Capture.Transcript("Nova Yes");
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.SpeechCalls.Should().Be(0);
        f.Accepted.Should().BeNull();
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
    }

    [Fact]
    public void Question_specific_name_metadata_is_optional_validated_and_part_of_exact_record_equality()
    {
        var admission = new QuestionVoiceAdmission(new("mic", "Synthetic"), 1, 1, 1);
        admission.ActivationName.Should().BeNull();
        var named = admission with { ActivationName = " Nova\tPrime " };
        named.ActivationName.Should().Be("Nova Prime");
        named.Should().NotBe(admission);
        (named with { ActivationName = "Atlas" }).Should().NotBe(named);
        (named with { ActivationName = null }).Should().Be(admission);
        var malformed = () => admission with { ActivationName = "Nova!" };
        malformed.Should().Throw<ArgumentException>();
    }
}
