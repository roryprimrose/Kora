using System.Collections.Immutable;
using System.Diagnostics;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private readonly Guid workOverviewHost = Guid.NewGuid();
    private long workOverviewEpoch;
    public static readonly TimeSpan WorkOverviewTimeout = TimeSpan.FromSeconds(10);
    public event Action<HostId<SessionIdentity>>? WorkOverviewInvalidated;

    private void InvalidateWorkOverview(HostId<SessionIdentity> session)
    {
        Interlocked.Increment(ref workOverviewEpoch);
        WorkOverviewInvalidated?.Invoke(session);
    }

    public async Task<SessionWorkOverviewPage> ReadWorkOverviewAsync(SessionListFilter filter, bool needsAnswer,
        SessionWorkOverviewCursor? cursor, CancellationToken token, SessionListSearch? query = null)
    {
        using var timeout = new CancellationTokenSource(WorkOverviewTimeout, overviewTime ?? TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            var result = await ReadAsync(() => ReadWorkOverviewCoreAsync(filter, needsAnswer, cursor, query, cancellation.Token),
                cancellation.Token).WaitAsync(cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException("Work overview storage read timed out; no partial page is current. Refresh.", exception);
        }
    }

    private async ValueTask<SessionWorkOverviewPage> ReadWorkOverviewCoreAsync(SessionListFilter filter, bool needsAnswer,
        SessionWorkOverviewCursor? cursor, SessionListSearch? query, CancellationToken readToken)
    {
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (query is not null && query.Filter != filter)
        {
            throw new ArgumentException("The overview query must use the selected lifecycle filter.", nameof(query));
        }
        var revision = access.ControlRevision;
        var epoch = Interlocked.Read(ref workOverviewEpoch);
        if (cursor is not null && (cursor.Host != workOverviewHost || cursor.AdmissionRevision != revision
            || cursor.SourceEpoch != epoch || cursor.After == Guid.Empty || cursor.Filter != filter
            || cursor.NeedsAnswer != needsAnswer || !string.Equals(cursor.QueryDigest, query?.Digest, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The overview continuation expired or belongs to another host/query/filter/source. Refresh.", nameof(cursor));
        }
        var page = await store.ReadMetadataPageAsync(cursor?.After, SessionWorkOverviewPage.MaximumSessions, readToken)
            .AsTask().WaitAsync(readToken).ConfigureAwait(false);
        if (page.Records.IsDefault || page.Records.Length > SessionWorkOverviewPage.MaximumSessions
            || page.Next is { } after && (page.Records.IsEmpty || after != page.Records[^1].Authority.SessionId.Value))
        {
            throw new InvalidDataException("Work overview requires a bounded advancing metadata source.");
        }
        var records = ImmutableArray.CreateBuilder<SessionWorkOverviewRow>();
        var previous = cursor?.After.ToString("D") ?? string.Empty;
        var scanned = 0;
        var nonmatches = 0;
        SessionWorkOverviewCursor? next = null;
        var limited = false;
        foreach (var entry in page.Records)
        {
            readToken.ThrowIfCancellationRequested();
            ValidateListEntry(entry);
            var id = entry.Authority.SessionId.Value;
            if (string.CompareOrdinal(previous, id.ToString("D")) >= 0)
            {
                throw new InvalidDataException("Work overview metadata requires strictly ordered canonical session IDs.");
            }
            previous = id.ToString("D");
            var continuation = new SessionWorkOverviewCursor(workOverviewHost, revision, epoch, id, filter, needsAnswer, query?.Digest);
            SessionWorkOverviewRow? row = null;
            if ((filter == SessionListFilter.All || entry.Authority.IsActive == (filter == SessionListFilter.Active))
                && (query is null || query.Matches(entry)))
            {
                row = await ObserveOverviewAsync(entry, needsAnswer, readToken).ConfigureAwait(false);
            }
            if (row is not null)
            {
                // Reserve maximum counters and continuation before consuming a row. Never skip the unconsumed ID.
                SessionWorkOverviewPage Candidate(SessionWorkOverviewRow candidate) => new(
                    records.ToImmutable().Add(candidate), SessionWorkOverviewPage.MaximumSessions,
                    SessionWorkOverviewPage.MaximumSessions, true, continuation);
                if (SessionWorkOverviewPage.GetSerializedSize(Candidate(row)) > SessionWorkOverviewPage.MaximumResultBytes)
                {
                    if (records.Count > 0) { limited = true; break; }
                    row = new(entry, SessionWorkOverviewAvailability.OutputBound, null);
                }
                records.Add(row);
            }
            else { nonmatches++; }
            scanned++;
            next = scanned < page.Records.Length || page.Next is not null ? continuation : null;
        }
        readToken.ThrowIfCancellationRequested();
        if (Interlocked.Read(ref workOverviewEpoch) != epoch)
        {
            throw new InvalidOperationException("Work overview source changed during observation. Refresh.");
        }
        var result = new SessionWorkOverviewPage(records.ToImmutable(), scanned, nonmatches, limited, next);
        _ = SessionWorkOverviewPage.Serialize(result);
        OverviewRead(logger, scanned, records.Count, nonmatches,
            records.Count(row => row.Availability != SessionWorkOverviewAvailability.Observed), limited);
        return result;
    }

    private async Task<SessionWorkOverviewRow?> ObserveOverviewAsync(
        SessionWorkspaceEntry entry, bool needsAnswer, CancellationToken token)
    {
        var parent = HostActivity.RequireCurrent();
        using var observation = HostActivity.BeginRoot(new(new(Guid.NewGuid()), entry.Authority.SessionId,
            new(Guid.NewGuid()), parent.Request.Origin), HostActivityLayer.Application, HostOperation.Storage,
            [new ActivityLink(parent.Activity!.Context)]);
        SessionWorkSnapshot snapshot;
        try
        {
            snapshot = await ReadWorkAsync(entry.Authority.SessionId, token).WaitAsync(token).ConfigureAwait(false);
            if (snapshot.Tasks.IsDefault || snapshot.Tasks.Length > SessionWorkSnapshot.MaximumRecords
                || snapshot.PendingQuestions.IsDefault || snapshot.PendingQuestions.Length > SessionWorkSnapshot.MaximumRecords
                || snapshot.QueueRecords.IsDefault || snapshot.OmittedTasks < 0 || snapshot.OmittedQuestions < 0
                || snapshot.OmittedQueueRecords < 0
                || snapshot.Tasks.Any(task => task.Task.Request.SessionId != entry.Authority.SessionId)
                || snapshot.PendingQuestions.Any(question => question.Key.Request.SessionId != entry.Authority.SessionId)
                || snapshot.QueueRecords.Any(record => record.Entry.Request.SessionId != entry.Authority.SessionId))
            {
                throw new InvalidDataException("Work overview records require bounded exact-session sources and explicit omissions.");
            }
        }
        catch (OperationCanceledException)
        {
            observation.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            observation.Complete(HostOperationOutcome.Failed);
            return new(entry, SessionWorkOverviewAvailability.ReadUnavailable, null);
        }
        catch
        {
            observation.Complete(HostOperationOutcome.Failed);
            throw;
        }
        token.ThrowIfCancellationRequested();
        if (snapshot.Session != entry)
        {
            observation.Complete(HostOperationOutcome.Failed);
            return new(entry, SessionWorkOverviewAvailability.SourceChanged, null);
        }
        // The admitted store owns policy/reasons. Do not recalculate eligibility or reinterpret omissions here.
        if (needsAnswer && snapshot.PendingQuestions.IsEmpty && snapshot.OmittedQuestions == 0)
        {
            observation.Complete(HostOperationOutcome.Completed);
            return null;
        }
        var work = new SessionWorkOverviewObservation(snapshot.AuthorityRevision, snapshot.ObservedAt,
            snapshot.Queue.Revision, snapshot.PendingCount, snapshot.PendingCapacity, snapshot.ExecutionSlots,
            snapshot.QueueRecords.Count(record => record.Entry.IsCurrent), snapshot.QueueRecords, snapshot.OmittedQueueRecords,
            [.. snapshot.Tasks.Select(task => new SessionWorkOverviewTask(task.Task.Request, task.Generation,
                task.Task.Revision, task.Task.State, task.CurrentSource))], snapshot.OmittedTasks,
            snapshot.PendingQuestions, snapshot.OmittedQuestions) { DispatchControl = snapshot.Queue.DispatchControl };
        observation.Complete(HostOperationOutcome.Completed);
        return new(entry, SessionWorkOverviewAvailability.Observed, work);
    }

}