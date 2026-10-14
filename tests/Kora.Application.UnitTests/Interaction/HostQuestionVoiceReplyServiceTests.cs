using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class HostQuestionVoiceReplyServiceTests
{
    [Fact]
    public async Task Cancellation_is_denied_before_retirement_callback_and_stale_native_predicates_fail_closed()
    {
        var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await f.Replies.OpenAsync(question, () => f.Visible, _ => { }, false, cancellation.Token);
        var admittedDuringCancellation = true;
        using var registration = f.Capture.CaptureToken.Register(() => admittedDuringCancellation = f.Capture.Admitted!());
        await cancellation.CancelAsync();
        admittedDuringCancellation.Should().BeFalse();
        f.Capture.Admitted!().Should().BeFalse();
        await f.Replies.DisposeAsync();
        f.Capture.Admitted!().Should().BeFalse();
        f.Host.Dispose();
    }

    [Fact]
    public async Task Fatal_native_release_failure_faults_quiescence_instead_of_leaving_a_hung_receipt()
    {
        var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
#pragma warning disable CA2201 // Inject the fatal boundary deterministically without inducing real memory pressure.
        f.Capture.StopFailure = new OutOfMemoryException("Synthetic exception; no memory pressure is induced");
#pragma warning restore CA2201
        f.Replies.Retire("Cancel");
        var quiescence = () => f.Replies.Quiescence;
        await quiescence.Should().ThrowAsync<OutOfMemoryException>();
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(1);
        var dispose = async () => await f.Replies.DisposeAsync();
        await dispose.Should().ThrowAsync<OutOfMemoryException>();
        f.Host.Dispose();
    }

    [Fact]
    public async Task Status_events_custom_answers_and_atomic_commit_fences_have_no_raw_transcript_archive()
    {
        await using var f = new QuestionReplyFixture();
        var changes = 0;
        f.Replies.Changed += (_, _) => changes++;
        var question = await f.Host.QuestionAsync(new("Bounded", QuestionKind.Text, [], maximumTextLength: 4));
        await f.OpenAsync(question);
        f.Capture.Transcript("Longer raw utterance");
        await f.Replies.Quiescence;
        f.Host.Store.Snapshot.Questions.Single().Draft.Should().BeNull();
        await f.OpenAsync(question);
        f.Capture.Transcript("Yes");
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Text.Should().Be("Yes");
        f.Host.Store.Commits.Select(commit => commit.Audit).Should().NotBeEmpty();
        changes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Cancellation_after_open_is_synchronous_and_cannot_reopen_while_native_cleanup_is_pending()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        using var token = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await f.Replies.OpenAsync(question, () => f.Visible, _ => { }, false, token.Token);
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await token.CancelAsync();
        f.Capture.IsListening.Should().BeFalse();
        f.Replies.IsActive.Should().BeFalse();
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(1);
        f.Capture.Closing.SetResult();
        await f.Replies.Quiescence;
    }

    [Fact]
    public async Task Cleanup_failure_is_explicit_fail_closed_and_never_allows_new_capture()
    {
        var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Capture.StopFailure = new IOException("Synthetic release failure");
        f.Replies.Retire("Cancel");
        var quiescence = () => f.Replies.Quiescence;
        await quiescence.Should().ThrowAsync<InvalidOperationException>();
        f.Replies.Status.Should().Contain("cleanup unconfirmed");
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(1);
        var dispose = async () => await f.Replies.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
        f.Host.Dispose();
    }

    [Fact]
    public async Task Late_start_and_expired_or_changed_callbacks_never_answer_or_extend_the_window()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var generation = f.Capture.Generation;
        f.Capture.Speech(generation - 1);
        f.Capture.Fail(generation - 1);
        f.Capture.Complete(generation - 1);
        f.Time.Now += TimeSpan.FromSeconds(15);
        f.Capture.Transcript("First");
        f.Capture.Speech();
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
        await f.OpenAsync(question);
        f.Visible = false;
        f.Capture.Speech();
        f.Capture.Transcript("First");
        f.Visible = true;
        f.Admission = null;
        f.Capture.Speech();
        f.Capture.Transcript("First");
        f.Admission = new(new("mic", "Synthetic"), 1, 1, 1);
        f.Time.Now = question.ExpiresAt;
        f.Capture.Speech();
        f.Capture.Transcript("First");
        f.Time.Timers.Last().Tick();
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
    }

    [Fact]
    public async Task Changed_native_generation_and_duplicate_completion_cannot_follow_a_submitted_reply()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var old = f.Capture.Generation;
        f.Capture.AdvanceGeneration();
        f.Capture.Transcript("First", old);
        f.Accepted.Should().BeNull();
        f.Replies.Retire("Cancel");
        await f.Replies.Quiescence;
        await f.OpenAsync(question);
        f.Capture.PreserveGenerationOnInvalidate = true;
        f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript("First");
        f.Capture.Transcript("Second");
        f.Capture.Complete();
        f.Capture.Fail();
        f.Dispatcher.Hold.SetResult();
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Choices.Should().Equal("a");
    }

    [Fact]
    public async Task New_host_question_retires_capture_but_foreign_revision_change_does_not_retarget()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var foreign = new HostQuestionKey(question.Key.Request, new(Guid.NewGuid()), new(1));
        await f.Host.RunAsync(() => f.Host.Questions.CancelAsync(foreign, CancellationToken.None));
        f.Replies.IsActive.Should().BeTrue();
        await f.Host.QuestionAsync();
        f.Replies.IsActive.Should().BeFalse();
        await f.Replies.Quiescence;
    }

    [Fact]
    public async Task Revocation_during_open_retires_before_generation_acknowledgement()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        f.Capture.BeforeOpen = () => f.Replies.Retire("Cancelled before native open");
        await f.OpenAsync(question);
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
    }

    [Fact]
    public async Task Explicit_reply_submits_once_to_original_schema_and_closes_capture()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Replies.Status.Should().Contain("Awaiting reply");
        f.Capture.IsListening.Should().BeTrue();
        f.Capture.Speech();
        f.Replies.Status.Should().Contain("Capturing reply");
        f.Capture.Transcript("First");
        await f.Replies.Quiescence;
        f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
        f.Accepted.Question!.Key.Should().Be(question.Key.Next());
        f.Accepted.Question.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.Capture.Transcript("Second");
        f.Host.Store.Snapshot.Questions.Single().Draft!.Choices.Should().Equal("a");
        f.Capture.Fail();
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.SpeechCalls.Should().Be(0);
    }

    [Fact]
    public async Task Playback_must_finish_successfully_for_same_visible_question_before_capture()
    {
        await using var f = new QuestionReplyFixture { Playback = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var question = await f.Host.QuestionAsync();
        var open = f.OpenAsync(question, afterPlayback: true);
        f.Speaking.Should().BeTrue();
        f.PlaybackEligible.Should().BeTrue();
        f.Capture.Opens.Should().Be(0);
        f.Capture.Transcript("First");
        f.Accepted.Should().BeNull();
        f.Replies.Status.Should().Contain("Microphone closed");
        f.Playback.SetResult();
        await open;
        f.Capture.Opens.Should().Be(1);
        f.Replies.Retire("Cancel");
        await f.Replies.Quiescence;
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("presenter")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task Playback_failure_or_changed_admission_never_opens_capture(string change)
    {
        await using var f = new QuestionReplyFixture { Playback = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var question = await f.Host.QuestionAsync();
        if (Is(change, "failure")) { f.ThrowPlayback = true; }
        var open = f.OpenAsync(question, afterPlayback: true);
        if (Is(change, "presenter")) { f.Visible = false; }
        if (Is(change, "privacy")) { f.Admission = null; }
        if (Is(change, "cancel")) { f.Replies.Retire("Cancel"); }
        f.Playback.TrySetResult();
        await open;
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.Accepted.Should().BeNull();
    }

    [Fact]
    public async Task Fifteen_second_speech_start_timeout_never_extends_with_silence_and_question_remains()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Time.Advance(TimeSpan.FromSeconds(14));
        f.Replies.Status.Should().Contain("1 seconds");
        f.Time.Advance(TimeSpan.FromSeconds(1));
        await f.Replies.Quiescence;
        f.Replies.IsActive.Should().BeFalse();
        f.Host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.Time.Timers[1].Tick();
        f.Capture.Speech();
        f.Capture.Transcript("First");
        f.Accepted.Should().BeNull();
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(2);
    }

    [Fact]
    public async Task Speech_start_before_deadline_uses_single_utterance_bounds_not_another_start_window()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Time.Advance(TimeSpan.FromSeconds(14));
        f.Capture.Speech();
        f.Time.Advance(TimeSpan.FromSeconds(2));
        f.Replies.Status.Should().Contain("maximum 60 seconds");
        f.Replies.IsActive.Should().BeTrue();
        f.Capture.Complete();
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("command")]
    [InlineData("session")]
    public async Task Mismatch_never_falls_through_to_other_routes(string mismatch)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Capture.Transcript(Is(mismatch, "schema") ? "Yes" : Is(mismatch, "command") ? "lock the machine" : "session new");
        await f.Replies.Quiescence;
        f.Replies.Status.Should().Contain("did not match");
        f.Accepted.Should().BeNull();
        f.Host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        f.Host.Store.Commits.Should().HaveCount(2);
    }

    [Fact]
    public async Task Generic_yes_cannot_open_or_approve_high_risk_question()
    {
        await using var f = new QuestionReplyFixture();
        var approval = await f.Host.RunAsync(() => f.Host.Authorization.PresentAsync(f.Host.Request, CancellationToken.None));
        QuestionVoiceAnswer.Parse(approval.Question!, "Yes").Should().BeNull();
        await f.OpenAsync(approval.Question!);
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("background")]
    [InlineData("readiness")]
    [InlineData("busy-capture")]
    [InlineData("duplicate")]
    [InlineData("changed-key")]
    [InlineData("generation")]
    public async Task Only_one_exact_current_foreground_question_can_capture(string reason)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        if (Is(reason, "background")) { f.Visible = false; }
        if (Is(reason, "readiness")) { f.Admission = null; }
        if (Is(reason, "busy-capture")) { f.Capture.NonQuiescent = true; }
        if (Is(reason, "duplicate")) { await f.OpenAsync(question); }
        if (Is(reason, "changed-key")) { question = question with { Key = question.Key.Next() }; }
        if (Is(reason, "generation")) { question = question with { SessionGeneration = new(2) }; }
        await f.OpenAsync(question);
        await f.Replies.Quiescence;
        f.Replies.IsActive.Should().BeFalse();
        f.Capture.Opens.Should().Be(Is(reason, "duplicate") ? 1 : 0);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("answer")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("device")]
    [InlineData("presenter")]
    [InlineData("recognizer")]
    public async Task Revocation_closes_and_fences_delayed_generations(string change)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        var generation = f.Capture.Generation;
        switch (change)
        {
            case "revision":
                await f.Host.RunAsync(() => f.Host.Questions.ReviseAsync(question.Key, question.Spec, question.ExpiresAt, CancellationToken.None)); break;
            case "answer":
                await f.Host.RunAsync(() => f.Host.Questions.SubmitAsync(question.Key, new(["b"]), RequestOrigin.LocalUi, CancellationToken.None)); break;
            case "cancel":
                f.Replies.Retire("Cancel"); break;
            case "expiry":
                f.Time.Advance(question.ExpiresAt - f.Time.Now); break;
            case "privacy":
                f.Admission = null; f.Replies.Revalidate(); break;
            case "call":
                f.Admission = f.Admission! with { CallRevision = 2 }; f.Replies.Revalidate(); break;
            case "device":
                f.Admission = f.Admission! with { TopologyRevision = 2 }; f.Replies.Revalidate(); break;
            case "presenter":
                f.Visible = false; f.Replies.Revalidate(); break;
            case "recognizer":
                f.Capture.Fail(); break;
        }
        f.Capture.IsListening.Should().BeFalse();
        f.Capture.Generation.Should().BeGreaterThan(generation);
        f.Capture.Transcript("First", generation);
        f.Capture.Speech(generation);
        f.Capture.Complete(generation);
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
    }

    [Fact]
    public async Task Delayed_ui_delivery_and_commit_gate_cannot_follow_new_admission()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        await f.OpenAsync(question);
        f.Dispatcher.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Capture.Transcript("First");
        f.Capture.Transcript("Second");
        f.Capture.Fail();
        f.Admission = null;
        f.Replies.Revalidate();
        f.Dispatcher.Hold.SetResult();
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
        f.Host.Store.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("ack")]
    [InlineData("commit")]
    public async Task Failures_are_explicit_and_do_not_claim_success(string failure)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        if (Is(failure, "open")) { f.Capture.OpenFailure = new IOException("Synthetic"); }
        if (Is(failure, "ack")) { f.Capture.RejectAcknowledgement = true; }
        await f.OpenAsync(question);
        if (Is(failure, "commit")) { f.Host.Store.FailStorage = true; f.Capture.Transcript("First"); }
        await f.Replies.Quiescence;
        f.Accepted.Should().BeNull();
        f.Capture.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_retires_pending_native_open_and_restart_never_replays()
    {
        var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync();
        f.Capture.Opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var open = f.OpenAsync(question);
        var disposal = f.Replies.DisposeAsync();
        await open;
        await disposal;
        f.Capture.Opens.Should().Be(0);
        await f.OpenAsync(question);
        f.Capture.Opens.Should().Be(0);
        f.Host.Dispose();
    }

    private static bool Is(string value, string expected) => string.Equals(value, expected, StringComparison.Ordinal);
}
