namespace Kora.Core.Dependencies;

public sealed record LocalModelQuestion(string Prompt, IReadOnlyList<string> Options, int Minimum = 1, int Maximum = 1)
{
    public const int MaximumFreeTextLength = 500;
}
