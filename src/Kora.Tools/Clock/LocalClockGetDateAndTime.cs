using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetDateAndTime(LocalClockService clock)
{
    internal LocalClockAnswer Execute() => clock.Read(LocalClockOperation.DateAndTime);
}
