using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed partial class QuestionVoiceAnswerTests
{
    [Theory]
    [InlineData("Yes", "yes")]
    [InlineData(" no ", "no")]
    [InlineData("1", "yes")]
    [InlineData("Option 2", "no")]
    public void Whole_utterance_only_selects_exact_schema(string text, string expected)
    {
        var question = Question(new("Choose", QuestionKind.SingleChoice, [new("yes", "Yes"), new("no", "No")]));
        QuestionVoiceAnswer.Parse(question, text)!.Choices.Should().Equal(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Yes and lock the machine")]
    [InlineData("Kora, switch session")]
    [InlineData("3")]
    public void Mismatch_has_no_command_or_other_question_fallback(string text)
    {
        var question = Question(new("Choose", QuestionKind.SingleChoice, [new("yes", "Yes"), new("no", "No")]));
        QuestionVoiceAnswer.Parse(question, text).Should().BeNull();
    }

    [Fact]
    public void Ambiguous_labels_and_multiselect_minimum_do_not_guess()
    {
        QuestionVoiceAnswer.Parse(Question(new("Choose", QuestionKind.SingleChoice,
            [new("a", "Same"), new("b", "Same")])), "Same").Should().BeNull();
        QuestionVoiceAnswer.Parse(Question(new("Choose", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second")], 2, 2)), "First").Should().BeNull();
    }

    [Fact]
    public void Custom_text_preserves_exact_bounded_answer_without_truncation()
    {
        var question = Question(new("Explain", QuestionKind.Text, [], maximumTextLength: 4));
        QuestionVoiceAnswer.Parse(question, " yes")!.Text.Should().Be(" yes");
        QuestionVoiceAnswer.Parse(question, "longer").Should().BeNull();
    }

    private static HostQuestionRecord Question(QuestionSpec spec) =>
        new(new(HostRequest.Create(RequestOrigin.LocalUi), new(Guid.NewGuid()), new(1)), spec, new(1), DateTimeOffset.MaxValue);
}
