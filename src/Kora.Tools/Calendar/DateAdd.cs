using Kora.Core.Calendar;

namespace Kora.Tools.Calendar;

public sealed class DateAdd
{
    internal DateOnly Execute(DateOnly start, int days) => CalendarDateArithmetic.Add(start, days);
}
