using Kora.Core.Clock;
using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public sealed record LocalClockCommand(LocalClockOperation Operation, bool IsActivated = false,
    LocalClockZone Zone = LocalClockZone.HostLocal)
{
    private static readonly string[] ReservedStems =
        ["what time", "what is today s date", "what is the date and time", "help local clock"];
    public const string Syntax = "Use what time is it, what is today's date, what is the date and time, or help local clock. "
        + "Append in UTC to any of the three answer requests for explicit UTC instead of the host-local timezone. "
        + "Answers use one host clock instant and host culture, including the UTC offset and timezone ID. "
        + "No network, model, location inference, calendar, reminder or scheduling operation is performed.";

    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(new[] { "what time is it", "what is today's date", "what is the date and time", "help local clock",
            "what time is it in utc", "what is today's date in utc", "what is the date and time in utc" });

    public static LocalClockCommand? Parse(string input, string assistantName)
    {
        var normalized = BuiltInCommandRouter.Normalize(input);
        var name = BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName)) + " ";
        var activated = Match(normalized) == LocalClockOperation.Invalid && normalized.StartsWith(name, StringComparison.Ordinal);
        if (activated)
        {
            var remainder = normalized[name.Length..];
            if (Match(remainder) != LocalClockOperation.Invalid || IsReserved(remainder)) { normalized = remainder; }
            else { activated = false; }
        }
        var operation = Match(normalized);
        if (operation == LocalClockOperation.Invalid && !IsReserved(normalized)) { return null; }
        return new(input.Length > 256 || input.Any(char.IsControl) ? LocalClockOperation.Invalid : operation, activated,
            normalized.EndsWith(" in utc", StringComparison.Ordinal) ? LocalClockZone.Utc : LocalClockZone.HostLocal);
    }

    private static LocalClockOperation Match(string normalized) => normalized switch
        {
            "what time is it" or "what time is it in utc" => LocalClockOperation.Time,
            "what is today s date" or "what is today s date in utc" => LocalClockOperation.Date,
            "what is the date and time" or "what is the date and time in utc" => LocalClockOperation.DateAndTime,
            "help local clock" => LocalClockOperation.Help,
            _ => LocalClockOperation.Invalid,
        };

    private static bool IsReserved(string normalized) =>
        ReservedStems
            .Any(stem => normalized.Equals(stem, StringComparison.Ordinal)
                || normalized.StartsWith(stem + " ", StringComparison.Ordinal));
}
