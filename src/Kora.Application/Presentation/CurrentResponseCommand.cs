using System.Text;
using Kora.Core.Configuration;

namespace Kora.Application.Presentation;

public sealed record CurrentResponseCommand(CurrentResponseAction? Action)
{
    public const string Syntax = "Use exactly dismiss current response, open current response details, or current response help.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["dismiss current response", "open current response details", "current response help"];

    public static CurrentResponseCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ','); }
        var words = new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var family = string.Join(' ', words.Take(2));
        if (!new[] { "dismiss current", "open current", "current response" }.Contains(family, StringComparer.OrdinalIgnoreCase))
        { return null; }
        if (Encoding.UTF8.GetByteCount(input) > 1024 || input.Any(char.IsControl))
        { return new((CurrentResponseAction?)null); }
        var index = FixedPhrases.ToList().FindIndex(phrase => phrase.Equals(text, StringComparison.OrdinalIgnoreCase));
        return new(index < 0 ? null : (CurrentResponseAction)index);
    }
}
