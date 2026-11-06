using System.Collections.Immutable;

using Kora.Core.Conversation;

namespace Kora.Core.Interaction;

/// <summary>One required ordinary clarification field, never an executable control or a credential input.</summary>
public sealed class QuestionField
{
    public const int MaximumFields = 4;
    public const int MaximumTextLength = 500;
    public const int MaximumTextUtf8Bytes = 2000;

    public QuestionField(string id, string label, QuestionKind kind, IEnumerable<QuestionOption> options,
        int maximumTextLength = 0, int maximumTextUtf8Bytes = 0)
    {
        Id = InteractionValidation.Identifier(id);
        ConversationText.Validate(label, submission: true);
        if (label.Length > 80 || kind is not (QuestionKind.Text or QuestionKind.SingleChoice))
        { throw new ArgumentException("Only bounded text and single-choice fields with bounded labels are supported.", nameof(kind)); }
        Label = label;
        Kind = kind;
        Options = options.Take(5).ToImmutableArray();
        MaximumLength = maximumTextLength;
        MaximumUtf8Bytes = maximumTextUtf8Bytes;
        if (kind == QuestionKind.Text
            ? !Options.IsEmpty || maximumTextLength is < 1 or > MaximumTextLength
                || maximumTextUtf8Bytes is < 1 or > MaximumTextUtf8Bytes
            : Options.Length is < 2 or > 4 || maximumTextLength != 0 || maximumTextUtf8Bytes != 0)
        { throw new ArgumentException("Field constraints exceed the finite clarification bounds.", nameof(options)); }
        foreach (var option in Options)
        {
            ConversationText.Validate(option.Label, submission: true);
            if (option.Label.Length > 80) { throw new ArgumentException("Field choice labels exceed 80 characters.", nameof(options)); }
        }
        if (Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != Options.Length
            || kind == QuestionKind.SingleChoice
                && !Kora.Core.Dependencies.LocalModelQuestionSpeech.AreOptionsUnambiguous(Options.Select(option => option.Label).ToArray()))
        { throw new ArgumentException("Field choices must have unique identities and unambiguous labels.", nameof(options)); }
    }

    public string Id { get; }
    public string Label { get; }
    public QuestionKind Kind { get; }
    public ImmutableArray<QuestionOption> Options { get; }
    public int MaximumLength { get; }
    public int MaximumUtf8Bytes { get; }

    public bool Accepts(QuestionFieldAnswer answer, bool submitting)
    {
        if (!string.Equals(Id, answer.FieldId, StringComparison.Ordinal)) { return false; }
        if (Kind == QuestionKind.SingleChoice)
        {
            return answer.Text is null && (answer.Choice is null ? !submitting
                : Options.Any(option => string.Equals(option.Id, answer.Choice, StringComparison.Ordinal)));
        }
        if (answer.Choice is not null || answer.Text is null || answer.Text.Length > MaximumLength) { return false; }
        try { ConversationText.Validate(answer.Text, submitting); }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        { return false; }
        return ConversationText.Measure(answer.Text).Utf8Bytes <= MaximumUtf8Bytes;
    }

    public QuestionSpec VoiceSpec() => new(Label, Kind, Options, maximumTextLength: MaximumLength);

    public bool Matches(QuestionField other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal) && string.Equals(Label, other.Label, StringComparison.Ordinal)
        && Kind == other.Kind && MaximumLength == other.MaximumLength && MaximumUtf8Bytes == other.MaximumUtf8Bytes
        && Options.SequenceEqual(other.Options);
}
