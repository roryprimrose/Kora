using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockGetDate(LocalClockService clock)
{
    internal LocalClockAnswer Execute() => clock.Read(LocalClockOperation.Date);
}
