namespace Kora.Core.Dependencies;

public sealed record LocalModelQuestion(string Prompt, IReadOnlyList<string> Options);
