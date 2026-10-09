using Kora.Core.Configuration;

namespace Kora.Core.Maintenance;

public static class MaintenanceCommandParser
{
    public const int MaximumLength = 128;
    public const int MaximumOutputLength = 8192;
    public const string Syntax = "Use maintenance status, maintenance review, or maintenance snooze. Cached metadata only; no check, browser, download or network consent.";
    public static IReadOnlyList<string> FixedPhrases { get; } = ["maintenance status", "maintenance review", "maintenance snooze"];

    public static MaintenanceCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("maintenance", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (transcript.Length > MaximumLength) { return MaintenanceCommand.Invalid; }
        return text.ToLowerInvariant() switch
        {
            "maintenance status" => MaintenanceCommand.Status,
            "maintenance review" => MaintenanceCommand.Review,
            "maintenance snooze" => MaintenanceCommand.Snooze,
            _ => MaintenanceCommand.Invalid,
        };
    }

    public static string BoundOutput(string output)
    {
        if (output.Length > MaximumOutputLength) { throw new InvalidDataException("Cached maintenance output exceeds its complete-response bound."); }
        return output;
    }
}
