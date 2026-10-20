using Kora.Core.Artifacts;
using Kora.Core.Configuration;

namespace Kora.Core.Commands;

/// <summary>Exact navigation only; no body, target or conversation authority.</summary>
public sealed record NewRequestOpenCommand(bool Accepted, bool ActivationPrefixed)
{
    public const string Phrase = "open new request";
    public const string Syntax = "Use exactly open new request (voice: current assistant name, open new request). No body, target, Save or Submit arguments.";
    public static IReadOnlyList<string> FixedPhrases { get; } = [Phrase];

    public static NewRequestOpenCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var trimmed = input.Trim();
        var normalized = BuiltInCommandRouter.Normalize(trimmed);
        var bare = normalized.StartsWith(Phrase, StringComparison.Ordinal);
        if (!bare && !normalized.StartsWith(
            BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName)) + " " + Phrase, StringComparison.Ordinal))
        { return null; }
        var text = bare ? trimmed : ArtifactCommandRouter.RemoveActivationPrefix(trimmed, assistantName);
        var prefixed = !string.Equals(text, trimmed, StringComparison.Ordinal);
        return new(string.Equals(text, Phrase, StringComparison.OrdinalIgnoreCase) && !input.Any(char.IsControl), prefixed);
    }
}
