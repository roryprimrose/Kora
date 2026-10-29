using Kora.Core.Configuration;

namespace Kora.Application.Configuration.RegisteredSettings;

public static class RegisteredSettingUndoCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["undo the last settings change", "confirm settings undo", "cancel settings undo"];

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
