using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record SessionQueueConfigurationCommand(AppearanceCommandOperation Operation,
    SessionQueueOption? Option = null, string? Value = null, string? Error = null)
{
    public const string PendingOptionId = "queue.pending-per-session";
    public const string SlotsOptionId = "queue.execution-slots";
    public const string LifetimeOptionId = "queue.pending-lifetime-minutes";
    public const string ActiveOptionId = "queue.active-budget-minutes";
    public const string Syntax = "list queue settings | get/status queue.pending-per-session | get/status queue.execution-slots"
        + " | set queue.pending-per-session to <integer 1-10> | set queue.execution-slots to <integer 1-2>"
        + " | get/status queue.pending-lifetime-minutes | set queue.pending-lifetime-minutes to <integer 1-120>"
        + " | get/status queue.active-budget-minutes | set queue.active-budget-minutes to <integer 1-60>"
        + " | reset queue.pending-per-session | reset queue.execution-slots | reset queue.pending-lifetime-minutes | reset queue.active-budget-minutes."
        + " Default/reset 10 pending, 1 slot, 30 pending minutes and 5 active minutes."
        + " Fixed synchronous read-only local-version work only; manual dispatch. Pending lifetime is captured only for newly enqueued entries"
        + " after confirmed activation. Active budget is captured only at future admission, including admission of old pending entries;"
        + " existing pending metadata and admitted deadlines never change. Apply-now, current-task extension and automatic dispatch unavailable.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list queue settings", "get " + PendingOptionId, "status " + PendingOptionId, "reset " + PendingOptionId,
            "get " + SlotsOptionId, "status " + SlotsOptionId, "reset " + SlotsOptionId,
            "get " + LifetimeOptionId, "status " + LifetimeOptionId, "reset " + LifetimeOptionId,
            "get " + ActiveOptionId, "status " + ActiveOptionId, "reset " + ActiveOptionId];

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
        foreach (var (id, option) in new[] { (PendingOptionId, SessionQueueOption.PendingPerSession),
            (SlotsOptionId, SessionQueueOption.ExecutionSlots), (LifetimeOptionId, SessionQueueOption.PendingLifetimeMinutes),
            (ActiveOptionId, SessionQueueOption.ActiveBudgetMinutes) })
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
