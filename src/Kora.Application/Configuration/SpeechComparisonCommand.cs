using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SpeechComparisonCommand(string Operation)
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["compare speech input", "test speech comparison", "cancel speech comparison"];

    public static SpeechComparisonCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        return FixedPhrases.FirstOrDefault(phrase => text.Equals(phrase, StringComparison.OrdinalIgnoreCase)) is { } operation
            ? new(operation) : null;
    }
}
