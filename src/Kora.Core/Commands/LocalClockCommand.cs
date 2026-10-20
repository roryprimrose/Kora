using Kora.Core.Clock;
using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public sealed record LocalClockCommand(LocalClockOperation Operation, bool IsActivated = false)
{
    private static readonly string[] ReservedStems =
        ["what time", "what is today s date", "what is the date and time", "help local clock"];
    public const string Syntax = "Use what time is it, what is today's date, what is the date and time, or help local clock. "
        + "Answers use one host clock instant, the host-local timezone and culture, including the UTC offset and timezone ID. "
        + "No network, model, location inference, calendar, reminder or scheduling operation is performed.";

    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(new[] { "what time is it", "what is today's date", "what is the date and time", "help local clock" });

    public static LocalClockCommand? Parse(string input, string assistantName)
    {
        var normalized = BuiltInCommandRouter.Normalize(input);
        var name = BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName)) + " ";
        var activated = Match(normalized) == LocalClockOperation.Invalid && normalized.StartsWith(name, StringComparison.Ordinal);
        if (activated) { normalized = normalized[name.Length..]; }
        var operation = Match(normalized);
        if (operation == LocalClockOperation.Invalid && !IsReserved(normalized)) { return null; }
        return new(input.Length > 256 || input.Any(char.IsControl) ? LocalClockOperation.Invalid : operation, activated);
    }

    private static LocalClockOperation Match(string normalized) => normalized switch
        {
            "what time is it" => LocalClockOperation.Time,
            "what is today s date" => LocalClockOperation.Date,
            "what is the date and time" => LocalClockOperation.DateAndTime,
            "help local clock" => LocalClockOperation.Help,
            _ => LocalClockOperation.Invalid,
        };

    private static bool IsReserved(string normalized) =>
        ReservedStems
            .Any(stem => normalized.Equals(stem, StringComparison.Ordinal)
                || normalized.StartsWith(stem + " ", StringComparison.Ordinal));
}
