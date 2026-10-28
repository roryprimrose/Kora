using AwesomeAssertions;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed partial class QuestionVoiceAnswerTests
{
    [Theory]
    [InlineData("Nova", "Yes", "yes")]
    [InlineData("Nova", "Nova yes", "yes")]
    [InlineData("Nova", " nOvA,\tYES ", "yes")]
    [InlineData("Nova", "Nova,2", "no")]
    [InlineData("Nova", "Nova option 2", "no")]
    [InlineData("Nova", "Nova\t1", "yes")]
    [InlineData("Nova Prime", "Nova Prime, Yes", "yes")]
    [InlineData("  Nova\tPrime ", "nova prime No", "no")]
    [InlineData("Nova-Prime", "NOVA-PRIME Yes", "yes")]
    [InlineData("O'Nova", "o'nova, Option 1", "yes")]
    public void One_current_validated_prefix_preserves_existing_exact_choice_forms(string name, string transcript, string expected)
    {
        var question = Question(new("Choose", QuestionKind.SingleChoice, [new("yes", "Yes"), new("no", "No")]));
        QuestionVoiceAnswer.Parse(question, transcript, name)!.Choices.Should().Equal(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Kora Yes")]
    [InlineData("Nov Yes")]
    [InlineData("NovaYes")]
    [InlineData("Nova-Yes")]
    [InlineData("Nova: Yes")]
    [InlineData("Nova")]
    [InlineData("Nova,")]
    [InlineData("Nova Nova Yes")]
    [InlineData("Nova, Nova, Yes")]
    [InlineData("Nova,, Yes")]
    [InlineData("Nova Yes.")]
    [InlineData("Nova option 01")]
    [InlineData("Nova 3")]
    [InlineData("Nova Yes and No")]
    public void Prefixes_never_invent_an_answer_or_expand_the_body_grammar(string transcript)
    {
        var question = Question(new("Choose", QuestionKind.SingleChoice, [new("yes", "Yes"), new("no", "No")]));
        QuestionVoiceAnswer.Parse(question, transcript, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("Nova Yes")]
    [InlineData("NovaPrime Yes")]
    [InlineData("Nova-Prime Yes")]
    [InlineData("Nova  Prime Yes")]
    public void Multiword_name_requires_its_exact_internal_spelling_and_boundary(string transcript)
    {
        QuestionVoiceAnswer.Parse(new QuestionSpec("Choose", QuestionKind.SingleChoice, [new("yes", "Yes")]),
            transcript, "Nova Prime").Should().BeNull();
    }

    [Fact]
    public void Missing_name_keeps_bare_only_behavior_without_an_invented_default()
    {
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice, [new("yes", "Yes")]);
        QuestionVoiceAnswer.Parse(spec, "Yes", null)!.Choices.Should().Equal("yes");
        QuestionVoiceAnswer.Parse(spec, "Kora Yes", null).Should().BeNull();
    }

    [Theory]
    [InlineData("Nova Yes", "literal")]
    [InlineData("Nova option 1", "ordinal-label")]
    [InlineData("Yes", "yes")]
    public void Bare_option_labels_take_precedence_over_prefix_interpretation(string transcript, string expected)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice,
            [new("yes", "Yes"), new("literal", "Nova Yes"), new("ordinal-label", "Nova option 1")]);
        QuestionVoiceAnswer.Parse(spec, transcript, "Nova")!.Choices.Should().Equal(expected);
    }

    [Fact]
    public void Ambiguous_bare_labels_cannot_be_reinterpreted_as_a_different_named_answer()
    {
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice,
            [new("yes", "Yes"), new("a", "Nova Yes"), new("b", "Nova Yes")]);
        QuestionVoiceAnswer.Parse(spec, "Nova Yes", "Nova").Should().BeNull();
        QuestionVoiceAnswer.Parse(spec, "Nova Same", "Nova").Should().BeNull();
        var duplicate = new QuestionSpec("Choose", QuestionKind.SingleChoice, [new("a", "Same"), new("b", "Same")]);
        QuestionVoiceAnswer.Parse(duplicate, "Nova Same", "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData(1, "yes")]
    [InlineData(2, null)]
    public void Named_multiple_choice_answers_still_require_the_existing_single_selection_bounds(int minimum, string? expected)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.MultipleChoice,
            [new("yes", "Yes"), new("no", "No")], minimum, 2);
        var answer = QuestionVoiceAnswer.Parse(spec, "Nova Yes", "Nova");
        if (expected is null) { answer.Should().BeNull(); }
        else { answer!.Choices.Should().Equal(expected); }
    }

    [Theory]
    [InlineData("  Nova,\tYes\r\n")]
    [InlineData("Nova Nova yes")]
    public void Free_text_is_never_stripped_trimmed_or_normalized(string transcript)
    {
        var spec = new QuestionSpec("Explain", QuestionKind.Text, [], maximumTextLength: transcript.Length);
        QuestionVoiceAnswer.Parse(spec, transcript, "Nova")!.Text.Should().Be(transcript);
        var bounded = new QuestionSpec("Explain", QuestionKind.Text, [], maximumTextLength: 3);
        QuestionVoiceAnswer.Parse(bounded, transcript, "Nova").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nova!")]
    [InlineData("One Two Three Four")]
    public void Malformed_captured_names_fail_explicitly_even_for_bare_answers(string name)
    {
        var spec = new QuestionSpec("Choose", QuestionKind.SingleChoice, [new("yes", "Yes")]);
        var parse = () => QuestionVoiceAnswer.Parse(spec, "Yes", name);
        parse.Should().Throw<ArgumentException>();
    }
}
