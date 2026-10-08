using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record DiagnosticRetentionCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "logging.sqlite-diagnostic-retention-days";
    public const string Syntax = "list logging settings | get " + OptionId + " | status " + OptionId
        + " | set " + OptionId + " to <integer 1-365> | reset " + OptionId
        + ". Default/reset 30. Future ordinary SQLite commits only; existing deadlines unchanged. Apply-now unavailable. "
        + "No audit, daily-file, session/history, grant or cleanup-schedule changes.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list logging settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId];

    public static DiagnosticRetentionCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list logging", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get logging.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status logging.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set logging.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset logging.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Logging input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list logging settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
        {
            return new(AppearanceCommandOperation.Set, text[prefix.Length..]);
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
