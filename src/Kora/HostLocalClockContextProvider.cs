using System.Globalization;
using Kora.Core.Clock;

namespace Kora;

internal sealed class HostLocalClockContextProvider : ILocalClockContextProvider
{
    public LocalClockContext GetCurrent() => new(TimeZoneInfo.Local, CultureInfo.CurrentCulture);
}
