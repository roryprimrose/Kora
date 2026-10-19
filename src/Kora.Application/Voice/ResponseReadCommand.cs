using System.Text;
using Kora.Core.Configuration;

namespace Kora.Application.Voice;

public sealed record ResponseReadCommand(bool IsValid)
{
    public static IReadOnlyList<string> FixedPhrases { get; } = ["read that aloud", "read current response"];

    public static bool Matches(string input, string assistantName) => Parse(input, assistantName)?.IsValid == true;

    public static ResponseReadCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',');
        }
        // Reservation is broader than acceptance so malformed controls cannot become a question answer or model input.
        var head = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        var prefix = string.Join(' ', head.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (!prefix.Equals("read that", StringComparison.OrdinalIgnoreCase)
            && !prefix.Equals("read current", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return new(Encoding.UTF8.GetByteCount(input) <= 1024 && !input.Any(char.IsControl)
            && FixedPhrases.Contains(text, StringComparer.OrdinalIgnoreCase));
    }
}
