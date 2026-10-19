using System.Text;
using Kora.Core.Configuration;

namespace Kora.Application.Voice;

public static class ResponseReadCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } = ["read that aloud", "read current response"];

    public static bool Matches(string input, string assistantName)
    {
        if (Encoding.UTF8.GetByteCount(input) > 1024 || input.Any(char.IsControl)) { return false; }
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',');
        }
        return FixedPhrases.Contains(text, StringComparer.OrdinalIgnoreCase);
    }
}
