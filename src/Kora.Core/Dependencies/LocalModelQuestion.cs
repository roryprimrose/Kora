using Kora.Core.Interaction;

namespace Kora.Core.Dependencies;

public sealed record LocalModelQuestion(string Prompt, IReadOnlyList<string> Options, int Minimum = 1, int Maximum = 1,
    IReadOnlyList<QuestionField>? Fields = null)
{
    public const int MaximumFreeTextLength = 500;
}
