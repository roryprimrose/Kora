using Kora.Core.Configuration;

namespace Kora.Application.Configuration.RegisteredSettings;

public static class RegisteredSettingSuggestionCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["review response mode suggestion", "confirm response mode change", "cancel response mode review"];

    public static IReadOnlyList<string> ResponseTimeoutPhrases { get; } =
        ["review response timeout suggestion", "confirm response timeout change", "cancel response timeout review"];

    public static RegisteredSettingSuggestionOperation? Parse(string input, string assistantName) =>
        Parse(input, assistantName, FixedPhrases);

    public static RegisteredSettingSuggestionOperation? ParseResponseTimeout(string input, string assistantName) =>
        Parse(input, assistantName, ResponseTimeoutPhrases);

    private static RegisteredSettingSuggestionOperation? Parse(string input, string assistantName, IReadOnlyList<string> phrases)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        for (var index = 0; index < phrases.Count; index++)
        {
            if (string.Equals(text, phrases[index], StringComparison.OrdinalIgnoreCase))
            { return (RegisteredSettingSuggestionOperation)index; }
        }
        return null;
    }
}
