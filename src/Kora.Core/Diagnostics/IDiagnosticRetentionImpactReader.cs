using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public interface IDiagnosticRetentionImpactReader
{
    event EventHandler? Changed;
    ValueTask<DiagnosticImpactSnapshot> ReadAsync(DiagnosticRetentionDays proposed, DateTimeOffset cutoff,
        HostRequest request, CancellationToken cancellationToken);
    bool IsCurrent(DiagnosticImpactSnapshot snapshot);
}
