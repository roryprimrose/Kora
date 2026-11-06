using Kora.Application.Configuration.RegisteredSettings;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public static class WindowsSpeechRateSuggestionCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["review rate suggestion", "confirm rate change", "cancel rate review"];

    public static RegisteredSettingSuggestionOperation? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        for (var index = 0; index < FixedPhrases.Count; index++)
        {
            if (string.Equals(text, FixedPhrases[index], StringComparison.OrdinalIgnoreCase))
            { return (RegisteredSettingSuggestionOperation)index; }
        }
        return null;
    }
}
