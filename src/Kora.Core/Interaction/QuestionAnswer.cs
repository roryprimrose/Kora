using System.Collections.Immutable;

namespace Kora.Core.Interaction;

public sealed record QuestionAnswer
{
    public QuestionAnswer(IEnumerable<string> choices, string? text = null, IEnumerable<QuestionFieldAnswer>? fields = null)
    {
        Choices = choices.ToImmutableArray();
        Text = text;
        Fields = fields?.Take(QuestionField.MaximumFields + 1).ToImmutableArray() ?? [];
    }

    public ImmutableArray<string> Choices { get; }
    public string? Text { get; }
    public ImmutableArray<QuestionFieldAnswer> Fields { get; }
}
