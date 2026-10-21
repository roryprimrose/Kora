using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetDateAndTime(LocalClockService clock)
{
    internal LocalClockAnswer Execute(LocalClockZone zone = LocalClockZone.HostLocal) => clock.Read(LocalClockOperation.DateAndTime, zone);
}
