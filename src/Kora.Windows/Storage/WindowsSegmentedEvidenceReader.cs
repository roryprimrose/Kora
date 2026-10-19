using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSegmentedEvidenceReader : IEvidenceReader
{
    private readonly Func<Func<EvidenceSegmentReadSet, EvidenceReadBatch>, CancellationToken, EvidenceReadBatch> readSegments;

    public WindowsSegmentedEvidenceReader(WindowsSegmentedEvidenceSink sink)
        : this((read, token) => sink.ReadSegments(read, token))
    {
    }

    internal WindowsSegmentedEvidenceReader(
        Func<Func<EvidenceSegmentReadSet, EvidenceReadBatch>, CancellationToken, EvidenceReadBatch> readSegments)
    {
        this.readSegments = readSegments;
    }

    public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request != request || request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Evidence reads require the admitted live local-UI request.");
        }
        return new(Task.Run(() =>
        {
            try
            {
                return readSegments(set => Read(set, query, checkpoint, request, now, cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested
                && exception is SqliteException or InvalidDataException)
            {
                throw new OperationCanceledException("The evidence read was cancelled during admission or query.",
                    exception, cancellationToken);
            }
        }, cancellationToken));
    }

    private static EvidenceReadBatch Read(EvidenceSegmentReadSet set, EvidenceQuery query,
        EvidenceReadCheckpoint? checkpoint, HostRequest request, DateTimeOffset now, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var prior = checkpoint?.Snapshot.Segmented;
        if (checkpoint is not null && (prior is null
            || prior.Version != SegmentedEvidenceSnapshot.CurrentVersion
            || !string.Equals(prior.StoreIdentity, set.StoreIdentity, StringComparison.Ordinal)
            || prior.InventoryGeneration > set.InventoryGeneration
            || prior.Segments.Count is 0 or > WindowsSegmentedEvidenceSink.MaximumSegments
            || prior.Segments.Select(segment => segment.Id).Distinct(StringComparer.Ordinal).Count() != prior.Segments.Count))
        {
            throw new InvalidDataException("The segmented evidence snapshot does not identify this retained store.");
        }
        var views = prior is null ? set.Segments : prior.Segments.Select(segment =>
            set.Segments.SingleOrDefault(view => string.Equals(view.Id, segment.Id, StringComparison.Ordinal)
                && view.AuditBase == segment.AuditBase)
            ?? throw new InvalidDataException("An evidence snapshot segment is no longer retained.")).ToArray();
        if (views.Count == 0 || views.Count > WindowsSegmentedEvidenceSink.MaximumSegments
            || views.Select(view => view.Id).Distinct(StringComparer.Ordinal).Count() != views.Count)
        {
            throw new InvalidDataException("The evidence segment inventory is empty or ambiguous.");
        }
        var readers = new List<SegmentReader>();
        try
        {
            foreach (var view in views)
            {
                token.ThrowIfCancellationRequested();
                var ceiling = prior?.Segments.Single(segment => string.Equals(segment.Id, view.Id, StringComparison.Ordinal)).Ceiling;
                if (view.AuditBase < 0 || ceiling?.Segmented is not null)
                {
                    throw new InvalidDataException("The evidence segment offset or ceiling is invalid.");
                }
                readers.Add(new(view, ceiling, query, checkpoint?.After, request, token));
            }
            var snapshot = checkpoint?.Snapshot ?? new EvidenceSnapshot(0, 0, 0, 0,
                Segmented: new(SegmentedEvidenceSnapshot.CurrentVersion, set.StoreIdentity, set.InventoryGeneration,
                    readers.Select(reader => new EvidenceSegmentSnapshot(reader.View.Id, reader.View.AuditBase,
                        reader.Ceiling)).ToArray()));
            var queue = new PriorityQueue<SegmentReader, EvidencePosition>(PositionComparer.Instance);
            foreach (var reader in readers)
            {
                if (reader.MoveNext(token)) { queue.Enqueue(reader, reader.Position!); }
            }
            var candidates = new List<EvidenceCandidate>();
            var scannedThrough = checkpoint?.After;
            var scanned = 0;
            EvidenceSegment Resolve(string trace, string span) =>
                ResolveSegment(readers, trace, span, query.SessionId, now, token);
            while (queue.TryDequeue(out var reader, out var position))
            {
                token.ThrowIfCancellationRequested();
                if (scanned == WindowsSqliteEvidenceReader.MaximumScannedRows)
                {
                    return new(snapshot, candidates, scannedThrough, HasMore: true, ScanLimitReached: true);
                }
                scanned++;
                var record = WindowsSqliteEvidenceReader.Project(reader.Connection, reader.Rows, position,
                    query.SessionId, reader.Ceiling, now, Resolve, reader.View.AuditBase);
                scannedThrough = position;
                if (WindowsSqliteEvidenceReader.Matches(record, query))
                {
                    candidates.Add(new(position, record));
                    if (candidates.Count == query.Limit + 1)
                    {
                        return new(snapshot, candidates, scannedThrough, HasMore: true, ScanLimitReached: false);
                    }
                }
                if (reader.MoveNext(token)) { queue.Enqueue(reader, reader.Position!); }
            }
            token.ThrowIfCancellationRequested();
            return new(snapshot, candidates, scannedThrough, HasMore: false, ScanLimitReached: false);
        }
        finally
        {
            foreach (var reader in readers) { reader.Dispose(); }
        }
    }

    private static EvidenceSegment ResolveSegment(IReadOnlyList<SegmentReader> readers, string trace, string span,
        HostId<SessionIdentity>? session, DateTimeOffset now, CancellationToken token)
    {
        EvidencePosition? first = null;
        EvidenceSegment? found = null;
        foreach (var reader in readers)
        {
            token.ThrowIfCancellationRequested();
            using var command = reader.Connection.CreateCommand();
            command.CommandText = """
                SELECT evidence_id,due_utc,committed_utc FROM activity_spans
                WHERE trace_id=$trace AND span_id=$span AND rowid<=$ceiling
                    AND ($session IS NULL OR session_id=$session)
                ORDER BY committed_utc,evidence_id LIMIT 1;
                """;
            command.Parameters.AddWithValue("$trace", trace);
            command.Parameters.AddWithValue("$span", span);
            command.Parameters.AddWithValue("$ceiling", reader.Ceiling.Span);
            command.Parameters.AddWithValue("$session", session is { } identity ? identity.Value.ToString("D") : DBNull.Value);
            using var rows = command.ExecuteReader();
            if (!rows.Read()) { continue; }
            var position = new EvidencePosition(rows.GetInt64(2), EvidenceSource.Span, rows.GetString(0), -1);
            if (first is not null && PositionComparer.Instance.Compare(first, position) <= 0) { continue; }
            first = position;
            found = new(trace, span,
                rows.GetInt64(1) <= now.UtcTicks ? EvidenceSegmentStatus.ExpiredButPresent : EvidenceSegmentStatus.Present,
                new(EvidenceSource.Span, new(Guid.ParseExact(position.Id, "D"))));
        }
        return found ?? new(trace, span, EvidenceSegmentStatus.MissingOrRemoved, null);
    }

    private sealed class PositionComparer : IComparer<EvidencePosition>
    {
        internal static readonly PositionComparer Instance = new();

        public int Compare(EvidencePosition? x, EvidencePosition? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var comparison = x.CommittedTicks.CompareTo(y.CommittedTicks);
            if (comparison == 0) { comparison = x.Source.CompareTo(y.Source); }
            if (comparison == 0) { comparison = string.CompareOrdinal(x.Id, y.Id); }
            return comparison == 0 ? x.Ordinal.CompareTo(y.Ordinal) : comparison;
        }
    }

    private sealed class SegmentReader : IDisposable
    {
        private readonly FileStream lease;
        private readonly SqliteCommand command;
        internal EvidenceSegmentReadView View { get; }
        internal SqliteConnection Connection { get; }
        internal EvidenceSnapshot Ceiling { get; }
        internal SqliteDataReader Rows { get; }
        internal EvidencePosition? Position { get; private set; }

        internal SegmentReader(EvidenceSegmentReadView view, EvidenceSnapshot? ceiling, EvidenceQuery query,
            EvidencePosition? after, HostRequest request, CancellationToken token)
        {
            View = view;
            lease = view.Sink.AcquireReadLease(token);
            try
            {
                Connection = view.Sink.OpenReadOnly(token);
                try
                {
                    Ceiling = ceiling ?? WindowsSqliteEvidenceReader.Snapshot(Connection);
                    if (ceiling is not null) { WindowsSqliteEvidenceReader.ValidateSnapshot(Connection, ceiling); }
                    command = WindowsSqliteEvidenceReader.CreateReadCommand(Connection, query, after, request, Ceiling);
                    try { Rows = command.ExecuteReader(); }
                    catch
                    {
                        command.Dispose();
                        throw;
                    }
                }
                catch
                {
                    Connection.Dispose();
                    throw;
                }
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        internal bool MoveNext(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Rows.Read()) { Position = null; return false; }
            Position = WindowsSqliteEvidenceReader.Position(Rows);
            return true;
        }

        public void Dispose()
        {
            Rows.Dispose();
            command.Dispose();
            Connection.Dispose();
            lease.Dispose();
        }
    }
}
