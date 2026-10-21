using System.Globalization;
using Kora.Application.Clock;
using Kora.Core.Clock;
using Kora.Tools.Clock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Clock;

internal sealed class ClockTestHost : TimeProvider, ILocalClockContextProvider
{
    public int Reads { get; private set; }
    public Action? BeforeRead { get; set; }
    public Exception? Failure { get; set; }
    public DateTimeOffset Instant { get; set; } = new(2025, 12, 31, 10, 0, 0, TimeSpan.Zero);

    public LocalClockWorkflow CreateWorkflow(ILogger<LocalClockWorkflow>? logger = null)
    {
        var service = new LocalClockService(this, this);
        return new(new(service), new(service), new(service), logger ?? NullLogger<LocalClockWorkflow>.Instance);
    }

    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        BeforeRead?.Invoke();
        if (Failure is { } failure) { throw failure; }
        return Instant;
    }

    public LocalClockContext GetCurrent()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.LongDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        return new(TimeZoneInfo.CreateCustomTimeZone("fixture/+14", TimeSpan.FromHours(14), "Fixture", "Fixture"), culture);
    }
}
