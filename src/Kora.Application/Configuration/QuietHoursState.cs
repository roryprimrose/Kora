using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record QuietHoursState(QuietHoursSchedule? Saved, QuietHoursSchedule? Effective, long Revision,
    bool SavedStateKnown, string? Recovery)
{
    public string Description => $"Quiet hours: saved {(SavedStateKnown ? Saved?.ToString() ?? "default Off" : "unknown")}; "
        + $"effective {Effective?.ToString() ?? "unavailable (proactive speech held)"}. "
        + (Recovery ?? "Selected days own the start of overnight intervals. Start is inclusive; end is exclusive. "
            + "Repeated local DST hours stay quiet inside the interval. Visual notices and requested answers are unchanged. "
            + "This setting never grants speech consent or enables listening.");
}
