using System.Collections.Immutable;

namespace Kora.Core.Interaction;

public sealed class QuestionSpec
{
    public QuestionSpec(string text, QuestionKind kind, IEnumerable<QuestionOption> options,
        int minimum = 1, int maximum = 1, int maximumTextLength = 0,
        string purpose = "clarification", string sourceId = "host")
    {
        Text = InteractionValidation.Text(text, 4096);
        Purpose = InteractionValidation.Identifier(purpose);
        SourceId = InteractionValidation.Identifier(sourceId);
        Kind = kind;
        Options = options.ToImmutableArray();
        Minimum = minimum;
        Maximum = maximum;
        MaximumTextLength = maximumTextLength;
        if (!Enum.IsDefined(kind) || minimum < 0 || maximum < minimum
            || Options.Length > 32 || Options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != Options.Length)
        {
            throw new ArgumentException("Invalid question constraints.", nameof(options));
        }
        if (kind == QuestionKind.Text)
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

    public bool Accepts(QuestionAnswer answer, bool submitting)
    {
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
}
