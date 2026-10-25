using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public static class PlaybackVolumeSuggestionCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["review volume suggestion", "confirm volume change", "cancel volume review"];

    public static PlaybackVolumeSuggestionOperation? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        for (var index = 0; index < FixedPhrases.Count; index++)
        {
            if (string.Equals(text, FixedPhrases[index], StringComparison.OrdinalIgnoreCase))
            { return (PlaybackVolumeSuggestionOperation)index; }
        }
        return null;
    }
}
