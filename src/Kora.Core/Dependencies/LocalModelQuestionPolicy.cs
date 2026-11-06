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
        foreach (var field in answer.Fields)
        {
            if (field.Text is { } value) { ConversationText.Validate(value); }
        }
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
            if (question.Fields is { } fields)
            {
                if (fields.Count is < 1 or > QuestionField.MaximumFields)
                { throw new InvalidDataException("A clarification form requires one to four fields."); }
                if (fields.Where((field, index) => !string.Equals(field.Id,
                        "field-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || field.Options.Where((option, optionIndex) => !string.Equals(option.Id,
                        "option-" + optionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)).Any()).Any())
                { throw new InvalidDataException("Model form identities must match the host-issued field and choice order."); }
                try { return new(question.Prompt, QuestionKind.Form, [], purpose: Purpose, sourceId: SourceId, fields: fields); }
                catch (ArgumentException exception) { throw new InvalidDataException("Clarification fields are invalid.", exception); }
            }
            return new(question.Prompt, QuestionKind.Text, [], maximumTextLength: LocalModelQuestion.MaximumFreeTextLength,
                purpose: Purpose, sourceId: SourceId);
        }
        if (question.Fields is not null) { throw new InvalidDataException("A form cannot also declare top-level choices."); }
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
