using Kora.Core.Conversation;
using Kora.Core.Interaction;

namespace Kora.Core.Dependencies;

/// <summary>Supported clarification shapes, with host-owned choice identities and no approval semantics.</summary>
public static class LocalModelQuestionPolicy
{
    public const string Purpose = "conversation-clarification";
    public const string SourceId = "model-question";

    public static void RequireAnswerContent(QuestionAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        foreach (var choice in answer.Choices) { ConversationText.Validate(choice, submission: true); }
        if (answer.Text is { } text) { ConversationText.Validate(text); }
    }

    public static QuestionSpec CreateSpec(LocalModelQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        ConversationText.Validate(question.Prompt, submission: true);
        if (question.Prompt.Length > 500 || question.Options is null || question.Options.Count is 1 or > 4)
        { throw new InvalidDataException("Only bounded text or two-to-four-choice clarifications are supported."); }
        if (question.Minimum < 0 || question.Maximum < 1 || question.Maximum < question.Minimum
            || (question.Options.Count == 0 ? question.Minimum != 1 || question.Maximum != 1
                : question.Maximum > question.Options.Count))
        { throw new InvalidDataException("Clarification selection bounds do not match the declared options."); }
        if (question.Options.Count == 0)
        {
            return new(question.Prompt, QuestionKind.Text, [], maximumTextLength: LocalModelQuestion.MaximumFreeTextLength,
                purpose: Purpose, sourceId: SourceId);
        }
        if (question.Options.Any(option => option is null))
        { throw new InvalidDataException("Clarification labels cannot be missing."); }
        foreach (var option in question.Options)
        {
            ConversationText.Validate(option, submission: true);
            if (option.Length > 80) { throw new InvalidDataException("Clarification labels exceed the supported bound."); }
        }
        if (!LocalModelQuestionSpeech.AreOptionsUnambiguous(question.Options))
        { throw new InvalidDataException("Clarification option labels must be unambiguous."); }
        return new(question.Prompt, question.Minimum == 1 && question.Maximum == 1
                ? QuestionKind.SingleChoice : QuestionKind.MultipleChoice,
            question.Options.Select((label, index) => new QuestionOption("option-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), label)),
            minimum: question.Minimum, maximum: question.Maximum,
            purpose: Purpose, sourceId: SourceId);
    }
}
