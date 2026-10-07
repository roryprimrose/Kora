using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed class WindowsEvidenceReader(
    WindowsSqliteEvidenceReader sqlite, WindowsDailyEvidenceReader daily,
    ICommittedAuthorityAuditReader? authority = null) : IEvidenceReader
{
    public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
        query.Source == EvidenceSource.AuthorityAudit
            ? (authority ?? throw new InvalidOperationException("Committed authority inspection is unavailable."))
                .ReadAsync(query, checkpoint, request, now, cancellationToken)
            : query.Source == EvidenceSource.CombinedLog
            ? ReadCombinedAsync(query, checkpoint, request, now, cancellationToken)
            : query.Source == EvidenceSource.DailyLog || query.Record?.Source == EvidenceSource.DailyLog
            ? daily.ReadAsync(query, checkpoint, request, now, cancellationToken)
            : sqlite.ReadAsync(query, checkpoint, request, now, cancellationToken);

    private async ValueTask<EvidenceReadBatch> ReadCombinedAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (checkpoint is not null && (checkpoint.Snapshot.DailySnapshotId is null
            || checkpoint.After is { Source: not (EvidenceSource.Log or EvidenceSource.DailyLog) }))
        {
            throw new InvalidDataException("The combined diagnostic checkpoint is not a source-qualified snapshot pair.");
        }
        var inDaily = checkpoint?.After?.Source == EvidenceSource.DailyLog;
        // Source-major ordering preserves both readers' authoritative positions; observations are not commit times.
        // Even after SQLite is exhausted, read-only admission and ceiling identity verification must still run.
        var sqliteCheckpoint = inDaily
            ? checkpoint! with { After = new(DateTimeOffset.MaxValue.UtcTicks, EvidenceSource.Log, "z", int.MaxValue) }
            : checkpoint;
        var database = await sqlite.ReadAsync(query with { Source = EvidenceSource.Log }, sqliteCheckpoint,
            request, now, cancellationToken).ConfigureAwait(false);
        var files = await daily.ReadAsync(query with { Source = EvidenceSource.DailyLog },
            checkpoint is null ? null : checkpoint with { After = inDaily ? checkpoint.After : null },
            request, now, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = database.Snapshot with { DailySnapshotId = files.Snapshot.DailySnapshotId };
        if (files.Snapshot.DailySnapshotId is null
            || files.Status is not (null or EvidencePageStatus.Partial or EvidencePageStatus.ScanLimitReached))
        {
            return files with
            {
                Snapshot = snapshot, Candidates = [], ScannedThrough = null, HasMore = false,
                UnavailableSources = [EvidenceSource.DailyLog],
            };
        }
        if (database.HasMore)
        {
            return database with
            {
                Snapshot = snapshot, DailyReport = files.DailyReport,
                ScanLimitReached = database.ScanLimitReached || files.ScanLimitReached,
                Status = database.ScanLimitReached ? EvidencePageStatus.ScanLimitReached : files.Status,
            };
        }
        var candidates = database.Candidates.Concat(files.Candidates).Take(query.Limit + 1).ToArray();
        return new(snapshot, candidates, candidates.LastOrDefault()?.Position ?? checkpoint?.After,
            files.HasMore || candidates.Length > query.Limit, files.ScanLimitReached, files.Status, files.DailyReport);
    }
}
