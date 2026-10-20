using Kora.Core.Artifacts;
using Kora.Core.Timekeeping;

namespace Kora.Core.Commands;

public sealed record StopwatchCommand(StopwatchOperation Operation, bool IsActivated = false)
{
    public const string Syntax = "stopwatch start | stopwatch status | stopwatch stop | stopwatch reset | stopwatch help";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(new[] { "stopwatch start", "stopwatch status", "stopwatch stop", "stopwatch reset", "stopwatch help" });

    public static StopwatchCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        var activated = false;
        // Preserve the reserved family before removing a name such as "Stopwatch".
        if (!FixedPhrases.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            var withoutName = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            if (BuiltInCommandRouter.Normalize(withoutName).StartsWith("stopwatch", StringComparison.Ordinal))
            {
                activated = !string.Equals(text, withoutName, StringComparison.Ordinal);
                text = withoutName;
            }
            else if (!string.Equals(text, withoutName, StringComparison.Ordinal)
                && (!string.Equals(assistantName.Split(' ')[0], "Stopwatch", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith(assistantName + ",", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith(assistantName + ":", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }
        if (!BuiltInCommandRouter.Normalize(text).StartsWith("stopwatch", StringComparison.Ordinal)) { return null; }
        if (input.Any(char.IsControl)) { return new(StopwatchOperation.Invalid); }
        return new(text.ToLowerInvariant() switch
        {
            "stopwatch start" => StopwatchOperation.Start,
            "stopwatch status" => StopwatchOperation.Status,
            "stopwatch stop" => StopwatchOperation.Stop,
            "stopwatch reset" => StopwatchOperation.Reset,
            "stopwatch help" => StopwatchOperation.Help,
            _ => StopwatchOperation.Invalid,
        }, activated);
    }
}
