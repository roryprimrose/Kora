using System.Collections.Immutable;

namespace Kora.Core.Interaction;

public sealed class QuestionSpec
{
    public QuestionSpec(string text, QuestionKind kind, IEnumerable<QuestionOption> options,
        int minimum = 1, int maximum = 1, int maximumTextLength = 0,
        string purpose = "clarification", string sourceId = "host", IEnumerable<QuestionField>? fields = null)
    {
        Text = InteractionValidation.Text(text, 4096);
        Purpose = InteractionValidation.Identifier(purpose);
        SourceId = InteractionValidation.Identifier(sourceId);
        Kind = kind;
        Options = options.ToImmutableArray();
        Fields = fields?.Take(QuestionField.MaximumFields + 1).ToImmutableArray() ?? [];
        Minimum = minimum;
        Maximum = maximum;
        MaximumTextLength = maximumTextLength;
        if (!Enum.IsDefined(kind) || minimum < 0 || maximum < minimum
            || Options.Length > 32 || Options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != Options.Length)
        {
            throw new ArgumentException("Invalid question constraints.", nameof(options));
        }
        if (kind == QuestionKind.Form)
        {
            Kora.Core.Conversation.ConversationText.Validate(text, submission: true);
            if (!Options.IsEmpty || minimum != 1 || maximum != 1 || maximumTextLength != 0
                || text.Length > 500
                || Fields.Length is < 1 or > QuestionField.MaximumFields
                || Fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != Fields.Length
                || Fields.Select(field => field.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Fields.Length
                || !(string.Equals(purpose, "clarification", StringComparison.Ordinal) && string.Equals(sourceId, "host", StringComparison.Ordinal)
                    || string.Equals(purpose, "conversation-clarification", StringComparison.Ordinal)
                        && string.Equals(sourceId, "model-question", StringComparison.Ordinal)))
            { throw new ArgumentException("Forms require finite unique fields and an ordinary clarification purpose.", nameof(fields)); }
        }
        else if (!Fields.IsEmpty)
        { throw new ArgumentException("Only form questions may declare fields.", nameof(fields)); }
        else if (kind == QuestionKind.Text)
        {
            if (Options.Length != 0 || minimum != 1 || maximum != 1 || maximumTextLength is < 1 or > 4096)
            {
                throw new ArgumentException("Text questions require a bounded nonempty answer.", nameof(maximumTextLength));
            }
        }
        else if (maximumTextLength != 0 || maximum > Options.Length || maximum == 0
            || (kind == QuestionKind.SingleChoice && (minimum != 1 || maximum != 1)))
        {
            throw new ArgumentException("Invalid choice bounds.", nameof(maximum));
        }
    }

    public string Text { get; }
    public string Purpose { get; }
    public string SourceId { get; }
    public QuestionKind Kind { get; }
    public ImmutableArray<QuestionOption> Options { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public int MaximumTextLength { get; }
    public ImmutableArray<QuestionField> Fields { get; }

    public bool Accepts(QuestionAnswer answer, bool submitting)
    {
        if (Kind == QuestionKind.Form)
        {
            return answer.Text is null && answer.Choices.IsEmpty && answer.Fields.Length <= Fields.Length
                && (!submitting || answer.Fields.Length == Fields.Length)
                && answer.Fields.Select(field => field.FieldId).Distinct(StringComparer.Ordinal).Count() == answer.Fields.Length
                && answer.Fields.All(value => Fields.Any(field => field.Accepts(value, submitting)));
        }
        if (!answer.Fields.IsEmpty) { return false; }
        if (Kind == QuestionKind.Text)
        {
            return answer.Choices.Length == 0 && answer.Text is not null
                && answer.Text.Length <= MaximumTextLength
                && (!submitting || !string.IsNullOrWhiteSpace(answer.Text));
        }
        return answer.Text is null && answer.Choices.Length <= Maximum
            && (!submitting || answer.Choices.Length >= Minimum)
            && answer.Choices.Distinct(StringComparer.Ordinal).Count() == answer.Choices.Length
            && answer.Choices.All(id => Options.Any(option => string.Equals(option.Id, id, StringComparison.Ordinal)));
    }

    public QuestionAnswer CanonicalAnswer(QuestionAnswer answer) => Kind == QuestionKind.Form
        ? new([], fields: Fields.Select(field => answer.Fields.Single(value => string.Equals(value.FieldId, field.Id, StringComparison.Ordinal))))
        : answer;

    public bool Matches(QuestionSpec other) =>
        string.Equals(Text, other.Text, StringComparison.Ordinal) && Kind == other.Kind
        && string.Equals(Purpose, other.Purpose, StringComparison.Ordinal)
        && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
        && Minimum == other.Minimum && Maximum == other.Maximum && MaximumTextLength == other.MaximumTextLength
        && Options.SequenceEqual(other.Options) && Fields.Length == other.Fields.Length
        && Fields.Zip(other.Fields).All(pair => pair.First.Matches(pair.Second));
}
