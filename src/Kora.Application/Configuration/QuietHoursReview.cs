using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed record QuietHoursReview(QuietHoursSchedule? Value, QuietHoursSchedule? Saved,
    long Revision, WorkSessionAuthorization Session, RequestOrigin Origin, SecurityAuditInitiator Initiator,
    Func<bool> Eligible, long CallRevision, DateTimeOffset Created, DateTimeOffset Expires)
{
    public string Description => $"Review quiet hours: {Value?.ToString() ?? "reset to default Off"}. "
        + $"Previous saved: {Saved?.ToString() ?? "default Off"}. "
        + "Only already-consented proactive completion speech is constrained; visual notices remain. "
        + "Suppressed completions will not replay. Confirm separately on the original channel within 60 seconds.";
}
