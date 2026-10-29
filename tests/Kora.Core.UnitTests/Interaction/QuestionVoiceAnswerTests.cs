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

    [Theory]
    [InlineData("First and Second")]
    [InlineData("1, 2")]
    [InlineData("option 1 and option 2")]
    [InlineData("option one and option two")]
    [InlineData("one AND two")]
    [InlineData("Kora, option 1 and option 2")]
    public void Multiple_answers_match_whole_explicit_list_without_command_interpretation(string spoken)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second"), new("c", "Third")], 2, 2);
        QuestionVoiceAnswer.Parse(spec, spoken, "Kora")!.Choices.Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("First and First")]
    [InlineData("First and Second and Third")]
    [InlineData("First and lock computer")]
    [InlineData("First and ")]
    [InlineData("First, ")]
    [InlineData("First")]
    [InlineData("none")]
    public void Multiple_answers_reject_duplicates_incomplete_lists_and_out_of_bound_selections(string spoken)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second"), new("c", "Third")], 2, 2);
        QuestionVoiceAnswer.Parse(spec, spoken).Should().BeNull();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("no options")]
    public void Optional_checkboxes_allow_explicit_empty_selection(string spoken)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second")], 0, 2);
        QuestionVoiceAnswer.Parse(spec, spoken)!.Choices.Should().BeEmpty();
    }

    [Fact]
    public void Exact_label_containing_list_separators_is_not_split()
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("a", "First and Second"), new("b", "Other")], 1, 2);
        QuestionVoiceAnswer.Parse(spec, "First and Second")!.Choices.Should().Equal("a");
    }

    [Fact]
    public void Larger_host_choice_schema_keeps_exact_numeric_matching_without_unsupported_word_guessing()
    {
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice,
            [new("a", "Alpha"), new("b", "Beta"), new("c", "Gamma"), new("d", "Delta"), new("e", "Epsilon")]);
        QuestionVoiceAnswer.Parse(spec, "5")!.Choices.Should().Equal("e");
        QuestionVoiceAnswer.Parse(spec, "five").Should().BeNull();
    }

    private static HostQuestionRecord Question(QuestionSpec spec) =>
        new(new(HostRequest.Create(RequestOrigin.LocalUi), new(Guid.NewGuid()), new(1)), spec, new(1), DateTimeOffset.MaxValue);
}
