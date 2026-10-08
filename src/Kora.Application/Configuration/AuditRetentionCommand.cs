using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record AuditRetentionCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "logging.audit-retention-days";
    public const string Syntax = "get " + OptionId + " | status " + OptionId
        + " | set " + OptionId + " to <integer 30-365> | reset " + OptionId
        + ". Default/reset 90. Future newly committed audit metadata only; existing deadlines unchanged. Apply-now unavailable. "
        + "No grant expiry/retention/eviction, session/history, ordinary logging, daily-file or cleanup changes.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get " + OptionId, "status " + OptionId, "reset " + OptionId];

    public static AuditRetentionCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status " + OptionId, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set " + OptionId, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset " + OptionId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Audit retention input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
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
