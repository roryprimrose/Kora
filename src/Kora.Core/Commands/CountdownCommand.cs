using System.Globalization;

using Kora.Core.Artifacts;

namespace Kora.Core.Commands;

public sealed record CountdownCommand(CountdownOperation Operation, int Seconds = 0)
{
    public bool IsActivated { get; init; }
    public const string Syntax = "Use exactly: timer start <seconds> | timer status | timer cancel | timer help. "
        + "Seconds must be whole ASCII digits from 1 to 86400. One visual-only timer for this run; no reminders or saved schedule.";
    public static IReadOnlyList<string> DiscoveryPhrases { get; } =
        Array.AsReadOnly(new[] { "timer start 60", "timer status", "timer cancel", "timer help" });

    public static bool IsValidSeconds(int seconds) => seconds is >= 1 and <= 86400;

    public static CountdownCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        var withoutActivation = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
        var activated = false;
        if (IsReserved(withoutActivation))
        {
            activated = !string.Equals(text, withoutActivation, StringComparison.Ordinal);
            text = withoutActivation;
        }
        if (!IsReserved(text)) { return null; }
        CountdownCommand Result(CountdownOperation operation, int seconds = 0) => new(operation, seconds) { IsActivated = activated };
        if (input.Length > 256 || input.Any(char.IsControl)) { return Result(CountdownOperation.Invalid); }
        if (text.Equals("timer status", StringComparison.OrdinalIgnoreCase)) { return Result(CountdownOperation.Status); }
        if (text.Equals("timer cancel", StringComparison.OrdinalIgnoreCase)) { return Result(CountdownOperation.Cancel); }
        if (text.Equals("timer help", StringComparison.OrdinalIgnoreCase)) { return Result(CountdownOperation.Help); }
        const string prefix = "timer start ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var number = text.AsSpan(prefix.Length);
            if (number.IndexOfAnyExceptInRange('0', '9') < 0
                && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && IsValidSeconds(seconds))
            {
                return Result(CountdownOperation.Start, seconds);
            }
        }
        return Result(CountdownOperation.Invalid);
    }

    private static bool IsReserved(string text)
    {
        var normalized = BuiltInCommandRouter.Normalize(text);
        return normalized.Equals("timer", StringComparison.Ordinal)
            || normalized.StartsWith("timer ", StringComparison.Ordinal);
    }
}
