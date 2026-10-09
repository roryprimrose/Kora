using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SessionQueueConfigurationCommand(AppearanceCommandOperation Operation,
    SessionQueueOption? Option = null, string? Value = null, string? Error = null)
{
    public const string PendingOptionId = "queue.pending-per-session";
    public const string SlotsOptionId = "queue.execution-slots";
    public const string Syntax = "list queue settings | get/status queue.pending-per-session | get/status queue.execution-slots"
        + " | set queue.pending-per-session to <integer 1-10> | set queue.execution-slots to <integer 1-2>"
        + " | reset queue.pending-per-session | reset queue.execution-slots. Default/reset 10 pending and 1 slot."
        + " Fixed synchronous read-only local-version work only; manual dispatch. Deadlines remain fixed/unavailable.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list queue settings", "get " + PendingOptionId, "status " + PendingOptionId, "reset " + PendingOptionId,
            "get " + SlotsOptionId, "status " + SlotsOptionId, "reset " + SlotsOptionId];

    public static SessionQueueConfigurationCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list queue", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset queue.", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Queue settings input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list queue settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        foreach (var (id, option) in new[] { (PendingOptionId, SessionQueueOption.PendingPerSession), (SlotsOptionId, SessionQueueOption.ExecutionSlots) })
        {
            if (text.Equals("get " + id, StringComparison.OrdinalIgnoreCase)
                || text.Equals("status " + id, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get, option); }
            if (text.Equals("reset " + id, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset, option); }
            var prefix = "set " + id + " to ";
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
            {
                return new(AppearanceCommandOperation.Set, option, text[prefix.Length..]);
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
