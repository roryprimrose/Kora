using System.Globalization;

using Kora.Core.Configuration;

namespace Kora.Core.Interaction;

public sealed record LocalEventCommand(LocalEventOperation Operation, Guid Id = default, long Revision = 0)
{
    public const string Syntax = "event status|review|dismiss|defer <exact-event-id> <revision>. Defer is 15 minutes, capped at the original event deadline. No question answer, speech, dispatch or maintenance Check/Open.";
    public static IReadOnlyList<string> FixedPhrases { get; } = ["event status", "event review", "event dismiss", "event defer"];
    public static LocalEventCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("event", StringComparison.OrdinalIgnoreCase)) { return null; }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (transcript.Length > 256 || parts.Length != 4
            || !string.Equals(parts[0], "event", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(parts[2], "D", out var id) || id == Guid.Empty
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision <= 0 || !string.Equals(parts[3], revision.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return new(LocalEventOperation.Invalid);
        }
        var operation = parts[1].ToLowerInvariant() switch
        {
            "status" => LocalEventOperation.Status,
            "review" => LocalEventOperation.Review,
            "dismiss" => LocalEventOperation.Dismiss,
            "defer" => LocalEventOperation.Defer,
            _ => LocalEventOperation.Invalid,
        };
        return new(operation, id, revision);
    }
}
