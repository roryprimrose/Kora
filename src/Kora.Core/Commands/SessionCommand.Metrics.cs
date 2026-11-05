using System.Text;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string MetricsSyntax = "session metrics <session-id> | show session metrics | "
        + "how many tokens have I used in this session | what is my context usage. "
        + "Without an ID, the exact current foreground conversation is required. "
        + "Voice requires the current assistant-name prefix. No model call, billing estimate or context compaction.";

    private static SessionCommand? ParseMetrics(string text)
    {
        var value = text.TrimEnd().TrimEnd('?').TrimEnd();
        var current = new[] { "show session metrics", "show token usage", "how many tokens have I used",
                "how many tokens have I used in this session", "what is my context usage",
                "session metrics" }.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (!current && !value.StartsWith("session metrics ", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(text) > MaximumInputBytes || text.Any(char.IsControl))
        { return new(SessionCommandOperation.Invalid, Error: MetricsSyntax); }
        if (current)
        { return new(SessionCommandOperation.Metrics); }
        return ExactId(value[16..].Trim(), out var id)
            ? new(SessionCommandOperation.Metrics, id)
            : new(SessionCommandOperation.Invalid, Error: MetricsSyntax);
    }
}
