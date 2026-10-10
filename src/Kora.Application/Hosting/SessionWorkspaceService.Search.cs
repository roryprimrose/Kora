using System.Collections.Immutable;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public Task<SessionHistorySearchPage> SearchHistoryAsync(HostId<SessionIdentity> session, string query,
        SessionHistorySearchCursor? cursor, int limit, CancellationToken token) =>
        ReadAsync(async () =>
        {
            session.Validate();
            SessionHistoryPage.ValidateLimit(limit);
            var search = new SessionHistorySearch(query);
            cursor?.History.Validate(session);
            if (cursor is not null && !string.Equals(cursor.QueryDigest, search.Digest, StringComparison.Ordinal))
            {
                throw new ArgumentException("The continuation belongs to another lexical query. Start a fresh search.", nameof(cursor));
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
                var page = await History.ReadHistoryAsync(session, continuation,
                    SessionHistoryPage.MaximumRecords, token).ConfigureAwait(false);
                if (page.SessionId != session || continuation is not null
                    && (page.Generation != continuation.Generation || page.Snapshot != continuation.Snapshot))
                {
                    throw new InvalidDataException("History search cannot cross exact session ownership.");
                }
                var after = continuation?.After ?? 0;
                result = new(session, page.Generation, page.Disposed, page.Snapshot, records.ToImmutable(),
                    scanned, gaps, omitted, null);
                foreach (var record in page.Records)
                {
                    token.ThrowIfCancellationRequested();
                    if (record.SessionId != session || record.Sequence <= after || record.Sequence > page.Snapshot)
                    {
                        throw new InvalidDataException("History search requires ordered exact-session receipts.");
                    }
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
            await History.ReadHistoryAsync(session, new(session, result.Generation, result.Snapshot, result.Snapshot),
                1, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _ = SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope) { HistorySearch = result });
            return result;
        }, token);
}
