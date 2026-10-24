using System.Globalization;
using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

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
        + " Natural exact aliases: show fixed read queue settings"
        + " | show/reset fixed read pending capacity | set fixed read pending capacity to <integer 1-10>"
        + " | show/reset fixed read execution slots | set fixed read execution slots to <integer 1-2>"
        + " | show/reset fixed read pending lifetime | set fixed read pending lifetime to <integer 1-120> minutes"
        + " | show/reset fixed read active budget | set fixed read active budget to <integer 1-60> minutes."
        + " Default/reset 10 pending, 1 slot, 30 pending minutes and 5 active minutes."
        + " Fixed synchronous read-only local-version work only; manual dispatch. Pending lifetime is captured only for newly enqueued entries"
        + " after confirmed activation. Active budget is captured only at future admission, including admission of old pending entries;"
        + " existing pending metadata and admitted deadlines never change. Apply-now, current-task extension and automatic dispatch unavailable.";
    private static readonly (string Id, SessionQueueOption Option, string Phrase, string Unit, int Maximum)[] Options =
        [(PendingOptionId, SessionQueueOption.PendingPerSession, "fixed read pending capacity", "", SessionQueueLimits.MaximumPendingPerSession),
            (SlotsOptionId, SessionQueueOption.ExecutionSlots, "fixed read execution slots", "", SessionQueueLimits.MaximumExecutionSlots),
            (LifetimeOptionId, SessionQueueOption.PendingLifetimeMinutes, "fixed read pending lifetime", " minutes", SessionQueueLimits.MaximumPendingLifetimeMinutes),
            (ActiveOptionId, SessionQueueOption.ActiveBudgetMinutes, "fixed read active budget", " minutes", SessionQueueLimits.MaximumActiveBudgetMinutes)];
    private static readonly string[] NaturalPrefixes = ["show fixed read", "set fixed read", "reset fixed read"];

    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(
        new[] { "list queue settings", "show fixed read queue settings" }
            .Concat(Options.SelectMany(option => new[] { "get " + option.Id, "status " + option.Id, "reset " + option.Id,
                "show " + option.Phrase, "reset " + option.Phrase }
                .Concat(Enumerable.Range(1, option.Maximum).Select(value =>
                    "set " + option.Phrase + " to " + value.ToString(CultureInfo.InvariantCulture) + option.Unit))))
            .ToArray());

    public static SessionQueueConfigurationCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        var natural = NaturalPrefixes.Any(prefix =>
            text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length])));
        if (!natural && !text.StartsWith("list queue", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set queue.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset queue.", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Queue settings input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list queue settings", StringComparison.OrdinalIgnoreCase)
            || text.Equals("show fixed read queue settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        foreach (var (id, option, phrase, unit, _) in Options)
        {
            if (text.Equals("show " + phrase, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get, option); }
            if (text.Equals("reset " + phrase, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset, option); }
            var naturalPrefix = "set " + phrase + " to ";
            if (text.StartsWith(naturalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = text[naturalPrefix.Length..];
                if (!value.EndsWith(unit, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
                value = value[..(value.Length - unit.Length)];
                try { _ = new SessionQueuePreferences().With(option, value); }
                catch (ArgumentOutOfRangeException) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
                return new(AppearanceCommandOperation.Set, option, value);
            }
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
