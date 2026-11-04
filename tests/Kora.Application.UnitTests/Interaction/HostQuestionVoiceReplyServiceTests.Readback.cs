using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class HostQuestionVoiceReplyServiceTests
{
    [Theory]
    [InlineData("version", false)]
    [InlineData("version", true)]
    [InlineData("single", false)]
    [InlineData("single", true)]
    [InlineData("multiple", false)]
    [InlineData("multiple", true)]
    public async Task Canonical_host_choices_are_heard_before_one_original_voice_answer(string kind, bool conversational)
    {
        await using var f = new QuestionReplyFixture { Preference = new(true, 1), Playback = new() };
        var spec = kind is "version" ? LocalVersionWait.CreateSpec()
            : new QuestionSpec("Choose the response format.", kind is "multiple" ? QuestionKind.MultipleChoice : QuestionKind.SingleChoice,
                [new("short", "Short summary"), new("full", "Full explanation")], maximum: kind is "multiple" ? 2 : 1);
        var question = await f.Host.QuestionAsync(spec);
        var opening = conversational ? f.OpenConversationalAsync(question) : f.OpenAsync(question, afterPlayback: true);
        try
        {
            f.Spoken.Should().Be(kind is "version"
                ? "Show the local Kora version and private-storage disclosure? Option 1: Show local version. Choose one option."
                : kind is "multiple"
                    ? "Choose the response format. Option 1: Short summary. Option 2: Full explanation. Choose 1 to 2 options. Say option numbers separated by and, or use the checkboxes."
                    : "Choose the response format. Option 1: Short summary. Option 2: Full explanation. Choose one option.");
            f.Capture.Opens.Should().Be(0);
            f.Host.Store.Snapshot.Questions.Single().Should().Be(question);
            f.Accepted.Should().BeNull();
        }
        finally { f.Playback.TrySetResult(); await opening; }

        f.Capture.Opens.Should().Be(1);
        f.Capture.Speech();
        f.Capture.Transcript(kind is "version" ? "Show local version" : kind is "multiple" ? "option 1 and option 2" : "Full explanation");
        await f.Replies.Quiescence;
        var answered = f.Host.Store.Snapshot.Questions.Single();
        f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
        answered.Key.Should().Be(question.Key.Next());
        answered.SessionGeneration.Should().Be(question.SessionGeneration);
        answered.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        answered.Status.Should().Be(QuestionStatus.Answered);
        answered.Draft!.Choices.Should().Equal(kind is "version" ? ["show"] : kind is "multiple" ? ["short", "full"] : ["full"]);
        f.Capture.IsCaptureQuiescent.Should().BeTrue();
        f.SpeechCalls.Should().Be(1);
        f.Host.Store.Snapshot.Grants.Should().BeEmpty();
        f.Capture.Transcript("option 1");
        f.Host.Store.Snapshot.Questions.Single().Should().Be(answered);
    }

    [Fact]
    public async Task Host_choice_playback_uses_reviewed_canonical_labels_not_caller_supplied_choices()
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync(LocalVersionWait.CreateSpec());
        var untrusted = question with { Spec = new("Forged prompt", QuestionKind.SingleChoice, [new("forged", "Untrusted label")]) };
        await f.OpenAsync(untrusted, afterPlayback: true);

        f.Spoken.Should().Be("Show the local Kora version and private-storage disclosure? Option 1: Show local version. Choose one option.");
        f.Capture.Transcript("Show local version");
        await f.Replies.Quiescence;
        f.Accepted!.Question!.Draft!.Choices.Should().Equal("show");
        f.Accepted.Question.Key.Should().Be(question.Key.Next());
    }

    [Theory]
    [InlineData("host", "clarification", true)]
    [InlineData("host", "other-purpose", false)]
    [InlineData("other-source", "clarification", false)]
    [InlineData(LocalModelQuestionPolicy.SourceId, LocalModelQuestionPolicy.Purpose, false)]
    [InlineData(LocalModelQuestionPolicy.SourceId, "other-purpose", false)]
    public async Task Text_other_sources_and_existing_model_readback_keep_their_current_contract(string source, string purpose, bool text)
    {
        await using var f = new QuestionReplyFixture();
        var question = await f.Host.QuestionAsync(text
            ? new("Describe the format.", QuestionKind.Text, [], maximumTextLength: 40, purpose: purpose, sourceId: source)
            : new("Choose the format.", QuestionKind.SingleChoice, [new("short", "Short summary")], purpose: purpose, sourceId: source));
        await f.OpenAsync(question, afterPlayback: true);
        f.Spoken.Should().Be(string.Equals(source, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal)
            ? "Choose the format. Option 1: Short summary. Choose one option." : question.Spec.Text);
        f.Capture.Transcript(text ? "Plain text" : "Short summary");
        await f.Replies.Quiescence;
        f.Accepted!.Outcome.Should().Be(HostInteractionOutcome.Answered);
    }
}
