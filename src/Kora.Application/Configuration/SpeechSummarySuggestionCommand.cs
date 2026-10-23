using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public static class SpeechSummarySuggestionCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["review speech limit suggestion", "confirm speech limit change", "cancel speech limit review"];

    public static SpeechSummarySuggestionOperation? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        for (var index = 0; index < FixedPhrases.Count; index++)
        {
            if (string.Equals(text, FixedPhrases[index], StringComparison.OrdinalIgnoreCase))
            { return (SpeechSummarySuggestionOperation)index; }
        }
        return null;
    }
}
