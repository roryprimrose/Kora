using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetTime(LocalClockService clock)
{
    internal LocalClockAnswer Execute(LocalClockZone zone = LocalClockZone.HostLocal) => clock.Read(LocalClockOperation.Time, zone);
}
