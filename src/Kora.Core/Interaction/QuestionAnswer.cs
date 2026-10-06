using System.Collections.Immutable;

namespace Kora.Core.Interaction;

public sealed record QuestionAnswer
{
    public QuestionAnswer(IEnumerable<string> choices, string? text = null)
    {
        Choices = choices.ToImmutableArray();
        Text = text;
    }

    public ImmutableArray<string> Choices { get; }
    public string? Text { get; }
}
