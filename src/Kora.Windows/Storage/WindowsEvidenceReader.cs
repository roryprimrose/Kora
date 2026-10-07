using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.Storage;

public sealed class WindowsEvidenceReader(
    WindowsSqliteEvidenceReader sqlite, WindowsDailyEvidenceReader daily) : IEvidenceReader
{
    public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        query.Source == EvidenceSource.DailyLog || query.Record?.Source == EvidenceSource.DailyLog
            ? daily.ReadAsync(query, checkpoint, request, now, cancellationToken)
            : sqlite.ReadAsync(query, checkpoint, request, now, cancellationToken);
}
