using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public interface IAuditRetentionImpactReader
{
    event EventHandler? Changed;
    ValueTask<AuditImpactResult> ReadAsync(AuditImpactSource source, AuditRetentionDays proposed,
        DateTimeOffset cutoff, HostRequest request, CancellationToken cancellationToken);
    bool IsCurrent(AuditImpactResult result);
}
