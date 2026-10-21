using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetDate(LocalClockService clock)
{
    internal LocalClockAnswer Execute(LocalClockZone zone = LocalClockZone.HostLocal) => clock.Read(LocalClockOperation.Date, zone);
}
