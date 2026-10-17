using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public Task<SessionHistorySearchPage> SearchHistoryAsync(HostId<SessionIdentity> session, string query,
        SessionHistorySearchCursor? cursor, int limit, CancellationToken token,
        SessionHistoryMetadataFilter metadata = default, SessionHistoryRangeFilter range = default,
        SessionHistoryCorrelationFilter correlation = default) =>
        ReadAsync(async () =>
        {
            session.Validate();
            SessionHistoryPage.ValidateLimit(limit);
            var search = new SessionHistorySearch(query, metadata, range, correlation);
            cursor?.History.Validate(session);
            if (cursor is not null && !string.Equals(cursor.QueryDigest, search.Digest, StringComparison.Ordinal))
            {
                throw new ArgumentException("The continuation belongs to another query, metadata, provenance or correlation predicate. Start a fresh search.", nameof(cursor));
            }
            var continuation = cursor?.History;
            var records = ImmutableArray.CreateBuilder<SessionHistoryEvent>();
            var scanned = 0;
            var gaps = 0;
            var omitted = 0;
            SessionHistorySearchPage result;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var readLimit = Math.Min(SessionHistoryPage.MaximumRecords, SessionHistorySearchPage.MaximumScannedRecords - scanned);
                var page = await History.ReadHistoryAsync(session, continuation,
                    readLimit, token).ConfigureAwait(false);
                ValidateSearchHistoryPage(page, session, continuation, readLimit, token);
                var after = continuation?.After ?? 0;
                result = new(session, page.Generation, page.Disposed, page.Snapshot, records.ToImmutable(),
                    scanned, gaps, omitted, null);
                foreach (var record in page.Records)
                {
                    token.ThrowIfCancellationRequested();
                    var next = new SessionHistorySearchCursor(new(session, page.Generation, page.Snapshot, record.Sequence), search.Digest);
                    if (search.Matches(record, token))
                    {
                        var candidate = result with { Records = records.ToImmutable().Add(record), Next = next,
                            Scanned = SessionHistorySearchPage.MaximumScannedRecords, Gaps = SessionHistorySearchPage.MaximumScannedRecords,
                            OmittedMatches = SessionHistorySearchPage.MaximumScannedRecords };
                        if (SessionCommandResult.GetSerializedSize(new("observed", SessionHistorySearchPage.Scope)
                            { HistorySearch = candidate }) > SessionCommand.MaximumResultBytes)
                        {
                            if (records.Count != 0) { break; }
                            omitted++;
                        }
                        else { records.Add(record); }
                    }
                    scanned++;
                    if (record.Baseline || record.Availability is SessionHistoryAvailability.Gap or SessionHistoryAvailability.Redacted or SessionHistoryAvailability.Unavailable) { gaps++; }
                    after = record.Sequence;
                    result = result with { Records = records.ToImmutable(), Scanned = scanned, Gaps = gaps,
                        OmittedMatches = omitted, Next = after < page.Snapshot ? next : null };
                    if (records.Count == limit || scanned == SessionHistorySearchPage.MaximumScannedRecords) { break; }
                }
                if (result.Next is null || records.Count == limit || scanned == SessionHistorySearchPage.MaximumScannedRecords
                    || after != page.Records.LastOrDefault()?.Sequence)
                {
                    break;
                }
                continuation = result.Next.History;
            }
            // Re-resolve the snapshot after scanning; lifecycle/retention changes cannot publish old content.
            var end = new SessionHistoryCursor(session, result.Generation, result.Snapshot, result.Snapshot);
            var resolved = await History.ReadHistoryAsync(session, end, 1, token).ConfigureAwait(false);
            ValidateSearchHistoryPage(resolved, session, end, 1, token);
            token.ThrowIfCancellationRequested();
            _ = SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope) { HistorySearch = result });
            return result;
        }, token);

    private static void ValidateSearchHistoryPage(SessionHistoryPage page, HostId<SessionIdentity> session,
        SessionHistoryCursor? cursor, int limit, CancellationToken token)
    {
        if (page.SessionId != session || page.Generation.Value <= 0 || page.Snapshot < 0
            || page.Records.IsDefault || page.Records.Length > limit
            || cursor is not null && (page.Generation != cursor.Generation || page.Snapshot != cursor.Snapshot))
        {
            throw new InvalidDataException("History search cannot cross exact session ownership or snapshot bounds.");
        }
        var after = cursor?.After ?? 0;
        foreach (var record in page.Records)
        {
            token.ThrowIfCancellationRequested();
            record.Validate();
            if (record.SessionId != session || record.Sequence <= after || record.Sequence > page.Snapshot
                || record.Generation.Value > page.Generation.Value)
            {
                throw new InvalidDataException("History search requires ordered exact-session receipts.");
            }
            after = record.Sequence;
        }
    }
}
