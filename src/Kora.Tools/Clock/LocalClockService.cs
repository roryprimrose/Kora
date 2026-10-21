using Kora.Core.Clock;

namespace Kora.Tools.Clock;

public sealed class LocalClockService(TimeProvider time, ILocalClockContextProvider context)
{
    internal LocalClockAnswer Read(LocalClockOperation operation, LocalClockZone zone)
    {
        var local = context.GetCurrent();
        return new(time.GetUtcNow(), local, operation, zone);
    }
}
