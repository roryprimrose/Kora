using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class HostQuestionVoiceReplyServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_off_or_unknown_preference_never_speaks_or_captures_and_manual_answer_is_unchanged(bool known)
    {
        await using var f = new QuestionReplyFixture { Preference = known ? new(false, 1) : null };
        var question = await f.Host.QuestionAsync();
        await f.OpenConversationalAsync(question);
        f.SpeechCalls.Should().Be(0);
        f.Capture.Opens.Should().Be(0);
        await f.OpenAsync(question);
        f.Capture.Transcript("First");
        await f.Replies.Quiescence;
        f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
    }

    [Fact]
    public async Task Missing_preference_binding_does_not_enable_automatic_replies()
    {
        await using var f = new QuestionReplyFixture();
        await using var replies = new HostQuestionVoiceReplyService(f.Host.Questions, f.Capture, f.Time, f.Dispatcher,
            () => f.Admission, (_, _, _) => throw new InvalidOperationException("No speech"),
            NullLogger<HostQuestionVoiceReplyService>.Instance);
        await replies.OpenConversationalAsync(await f.Host.QuestionAsync(), static () => true, _ => { },
            TestContext.Current.CancellationToken);
        replies.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("host", "clarification")]
    [InlineData("host", "local-version")]
    [InlineData(LocalModelQuestionPolicy.SourceId, LocalModelQuestionPolicy.Purpose)]
    public async Task Exact_successful_playback_opens_one_prefix_free_schema_answer_only(string source, string purpose)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1), Playback = new() };
        var question = await f.Host.QuestionAsync(new("Exact question", QuestionKind.SingleChoice,
            [new("a", "First"), new("b", "Second")], purpose: purpose, sourceId: source));
        var opening = f.OpenConversationalAsync(question);
        f.Speaking.Should().BeTrue();
        f.Capture.Opens.Should().Be(0);
        f.Capture.Transcript("First");
        f.Playback.SetResult();
        await opening;
        f.Capture.Opens.Should().Be(1);
        var generation = f.Capture.Generation;
        f.Capture.Transcript("First", generation - 1);
        f.Accepted.Should().BeNull();
        f.Capture.Transcript("First");
        f.Capture.Transcript("Second");
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Choices.Should().Equal("a");
        f.Host.Store.Snapshot.Questions.Single().AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        f.Time.Advance(TimeSpan.FromMinutes(2));
        f.Capture.Opens.Should().Be(1);
        f.SpeechCalls.Should().Be(1);
        f.Capture.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("egress")]
    [InlineData("foreign-source")]
    [InlineData("foreign-purpose")]
    [InlineData("model-egress")]
    public async Task Approval_consent_or_unadmitted_source_cannot_open_automatic_capture(string kind)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1) };
        var question = kind is "approval"
            ? (await f.Host.RunAsync(() => f.Host.Authorization.PresentAsync(f.Host.Request, CancellationToken.None))).Question!
            : await f.Host.QuestionAsync(new("Native only", QuestionKind.Text, [], maximumTextLength: 10,
                sourceId: kind is "foreign-source" ? "provider" : kind is "model-egress" ? LocalModelQuestionPolicy.SourceId : "host",
                purpose: kind is "foreign-source" ? "clarification" : "egress-consent"));
        await f.OpenConversationalAsync(question);
        f.SpeechCalls.Should().Be(0);
        f.Capture.Opens.Should().Be(0);
    }

    [Theory]
    [InlineData("preference")]
    [InlineData("disable")]
    [InlineData("unknown")]
    [InlineData("target")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("ownership")]
    [InlineData("microphone")]
    [InlineData("device")]
    [InlineData("question")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("failure")]
    public async Task Automatic_playback_admission_replacement_never_opens_capture(string boundary)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1), Playback = new() };
        var question = await f.Host.QuestionAsync();
        if (boundary is "failure") { f.ThrowPlayback = true; }
        var opening = f.OpenConversationalAsync(question);
        switch (boundary)
        {
            case "preference": f.Preference = new(true, 2); break;
            case "disable": f.Preference = new(false, 2); break;
            case "unknown": f.Preference = null; break;
            case "target": f.Visible = false; break;
            case "call": f.Admission = f.Admission! with { CallRevision = 2 }; break;
            case "privacy": case "ownership": case "microphone": f.Admission = null; break;
            case "device": f.Admission = f.Admission! with { TopologyRevision = 2 }; break;
            case "question":
                await f.Host.RunAsync(() => f.Host.Questions.ReviseAsync(question.Key, question.Spec, question.ExpiresAt, CancellationToken.None)); break;
            case "source": await f.Host.QuestionAsync(); break;
            case "cancel": f.Replies.Retire("Cancel"); break;
            case "expiry": f.Time.Advance(question.ExpiresAt - f.Time.Now); break;
        }
        f.Replies.Revalidate();
        f.Playback.TrySetResult();
        await opening;
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.Accepted.Should().BeNull();
    }

    [Fact]
    public async Task Canonical_source_is_checked_again_after_host_review_not_trusted_from_the_caller()
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1) };
        var canonical = await f.Host.QuestionAsync(new("Consent", QuestionKind.Text, [], maximumTextLength: 10, purpose: "egress-consent"));
        await f.OpenConversationalAsync(canonical with { Spec = new("Forged", QuestionKind.Text, [], maximumTextLength: 10) });
        await f.Replies.Quiescence;
        f.Capture.Opens.Should().Be(0);
        f.SpeechCalls.Should().Be(0);
    }

    [Fact]
    public async Task Preference_replacement_retires_capture_and_actual_shutdown_joins_the_native_release()
    {
        var f = new QuestionReplyFixture { Preference = new(true, 1) };
        var question = await f.Host.QuestionAsync();
        await f.OpenConversationalAsync(question);
        var generation = f.Capture.Generation;
        f.Capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Preference = new(true, 2);
        f.Replies.Revalidate();
        f.Capture.IsListening.Should().BeFalse();
        var closing = f.DisposeAsync().AsTask();
        closing.IsCompleted.Should().BeFalse();
        f.Capture.Transcript("First", generation);
        f.Capture.Speech(generation);
        f.Accepted.Should().BeNull();
        f.Capture.Closing.SetResult();
        await closing;
        f.Capture.Opens.Should().Be(1);
    }
}
