using Kora.Core.Calendar;

namespace Kora.Tools.Calendar;

public sealed class DateDifference
{
    internal int Execute(DateOnly start, DateOnly end) => CalendarDateArithmetic.Difference(start, end);
}
