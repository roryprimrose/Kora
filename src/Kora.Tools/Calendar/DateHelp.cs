namespace Kora.Tools.Calendar;

public sealed class DateHelp
{
    internal string Execute() => "Calendar dates only; no clock, timezone, holidays, business days, scheduling or reminders. "
        + "Difference is signed end minus start in whole calendar days: start excluded, end arithmetic, not an inclusive count. "
        + "Examples: date difference 2000-02-28 2000-03-01 => 2; date add 2000-03-01 -1 => 2000-02-29.";
}
