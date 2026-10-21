using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetTime(LocalClockService clock)
{
    internal LocalClockAnswer Execute() => clock.Read(LocalClockOperation.Time);
}
