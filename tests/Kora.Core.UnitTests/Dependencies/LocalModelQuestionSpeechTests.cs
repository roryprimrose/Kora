using AwesomeAssertions;

using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class LocalModelQuestionSpeechTests
{
    private const string Assistant = "Nova";

    [Fact]
    public void AreOptionsUnambiguous_rejects_null_wrong_count_and_collisions()
    {
        var nullOptions = () => LocalModelQuestionSpeech.AreOptionsUnambiguous(null!);
        nullOptions.Should().Throw<ArgumentNullException>();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["only"]).Should().BeFalse();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["a", "b", "c", "d", "e"]).Should().BeFalse();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["Alpha", "alpha"]).Should().BeFalse();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["Alpha", "one"]).Should().BeFalse();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["option two", "Beta"]).Should().BeFalse();
        LocalModelQuestionSpeech.AreOptionsUnambiguous(["Alpha", "Beta", "Gamma", "Delta"]).Should().BeTrue();
    }

    [Fact]
    public void GetPhrases_includes_numbered_named_and_answer_phrases_but_not_unsafe_option_text()
    {
        var question = new LocalModelQuestion("Choose", ["Alpha", "Use /tmp"]);

        var phrases = LocalModelQuestionSpeech.GetPhrases(question, Assistant).ToArray();

        phrases.Should().Contain(["1", "option one", "Alpha", "Nova Alpha", "2",
            "second option", "cancel question", "Nova cancel question", "accept", "Nova deny"]);
        phrases.Should().NotContain("Use /tmp");
        phrases.Should().NotContain("Nova Use /tmp");
        var nullQuestion = () => LocalModelQuestionSpeech.GetPhrases(null!, Assistant).ToArray();
        nullQuestion.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("alpha", 0)]
    [InlineData("Nova, ALPHA!", 0)]
    [InlineData("2", 1)]
    [InlineData("Nova option two", 1)]
    [InlineData("second option", 1)]
    [InlineData("not an option", -1)]
    [InlineData("Other alpha", -1)]
    public void Match_selects_named_numbered_and_spoken_options(string transcript, int expected)
    {
        var question = new LocalModelQuestion("Choose", ["Alpha", "Beta"]);

        LocalModelQuestionSpeech.Match(transcript, question, Assistant).Should().Be(expected);
    }

    [Theory]
    [InlineData("yes", 0)]
    [InlineData("Nova approve", 0)]
    [InlineData("accept", 0)]
    [InlineData("no", 1)]
    [InlineData("deny", 1)]
    [InlineData("reject", 1)]
    public void Match_resolves_unique_affirmative_and_negative_options(string transcript, int expected)
    {
        var question = new LocalModelQuestion("Proceed?", ["Approve", "Reject"]);

        LocalModelQuestionSpeech.Match(transcript, question, Assistant).Should().Be(expected);
    }

    [Fact]
    public void Match_does_not_guess_when_labels_are_missing_or_ambiguous()
    {
        LocalModelQuestionSpeech.Match("yes",
            new LocalModelQuestion("Choose", ["Alpha", "Beta"]), Assistant).Should().Be(-1);
        LocalModelQuestionSpeech.Match("yes",
            new LocalModelQuestion("Choose", ["Accept", "Approve"]), Assistant).Should().Be(-1);
        LocalModelQuestionSpeech.Match("no",
            new LocalModelQuestion("Choose", ["Alpha", "Beta"]), Assistant).Should().Be(-1);
        LocalModelQuestionSpeech.Match("no",
            new LocalModelQuestion("Choose", ["Reject", "Deny"]), Assistant).Should().Be(-1);
        LocalModelQuestionSpeech.Match("approve",
            new LocalModelQuestion("Choose", ["Accept", "Decline"]), Assistant).Should().Be(0);
        LocalModelQuestionSpeech.Match("reject",
            new LocalModelQuestion("Choose", ["Accept", "Deny"]), Assistant).Should().Be(1);
        var invalidTranscript = () => LocalModelQuestionSpeech.Match("", new("Choose", ["a", "b"]), Assistant);
        invalidTranscript.Should().Throw<ArgumentException>();
        var nullQuestion = () => LocalModelQuestionSpeech.Match("yes", null!, Assistant);
        nullQuestion.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("cancel question", true)]
    [InlineData("Nova never mind", true)]
    [InlineData("reject", true)]
    [InlineData("approve", false)]
    [InlineData("Another reject", false)]
    public void IsCancellation_recognizes_only_explicit_cancellation(string transcript, bool expected)
    {
        LocalModelQuestionSpeech.IsCancellation(transcript, Assistant).Should().Be(expected);
    }

    [Fact]
    public void IsCancellation_rejects_blank_transcripts()
    {
        var action = () => LocalModelQuestionSpeech.IsCancellation(" ", Assistant);
        action.Should().Throw<ArgumentException>();
    }
}
