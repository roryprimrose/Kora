using System.Collections.Immutable;

using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private readonly Guid listSearchHost = Guid.NewGuid();

    public Task<SessionListSearchPage> SearchListAsync(SessionListSearchKind kind, SessionListFilter filter,
        string query, SessionListSearchCursor? cursor, int limit, CancellationToken token,
        long? minimumGeneration = null, long? maximumGeneration = null,
        long? minimumMetadataRevision = null, long? maximumMetadataRevision = null,
        SessionNameAvailability nameAvailability = SessionNameAvailability.Any,
        long? minimumNameLength = null, long? maximumNameLength = null) =>
        ReadAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var search = new SessionListSearch(kind, filter, query, minimumGeneration, maximumGeneration,
                minimumMetadataRevision, maximumMetadataRevision, nameAvailability, minimumNameLength, maximumNameLength);
            SessionListSearchPage.ValidateLimit(limit);
            cursor?.Validate(listSearchHost, access.ControlRevision, search);
            if (search.ExactId is { } id)
            {
                var entry = await store.ReadMetadataAsync(new(id), token).ConfigureAwait(false);
                SessionListSearch.ValidateEntry(entry);
                if (entry.Authority.SessionId.Value != id)
                {
                    throw new InvalidDataException("Exact metadata lookup returned another session.");
                }
                token.ThrowIfCancellationRequested();
                return new(search.Matches(entry) ? [entry] : [], 1, entry.Metadata is null ? 1 : 0, false, null);
            }

            var page = await store.ReadMetadataPageAsync(cursor?.After,
                SessionListSearchPage.MaximumScannedRecords, token).ConfigureAwait(false);
            if (page.Records.IsDefault || page.Records.Length > SessionListSearchPage.MaximumScannedRecords
                || page.Next is not null && (page.Records.IsEmpty || page.Next == Guid.Empty))
            {
                throw new InvalidDataException("Metadata continuation requires a bounded advancing source page.");
            }
            var previous = cursor?.After.ToString("D") ?? string.Empty;
            foreach (var entry in page.Records)
            {
                token.ThrowIfCancellationRequested();
                SessionListSearch.ValidateEntry(entry);
                var current = entry.Authority.SessionId.Value.ToString("D");
                if (string.CompareOrdinal(previous, current) >= 0)
                {
                    throw new InvalidDataException("Metadata requires strictly ordered canonical session IDs.");
                }
                previous = current;
            }
            if (page.Next is not null && page.Next != page.Records[^1].Authority.SessionId.Value)
            {
                throw new InvalidDataException("Metadata continuation must identify the last validated source row.");
            }

            var records = ImmutableArray.CreateBuilder<SessionWorkspaceEntry>();
            var scanned = 0;
            var unnamed = 0;
            SessionListSearchCursor? next = null;
            var limited = false;
            foreach (var entry in page.Records)
            {
                token.ThrowIfCancellationRequested();
                var continuation = new SessionListSearchCursor(listSearchHost, access.ControlRevision,
                    entry.Authority.SessionId.Value, search.Digest);
                if (search.Matches(entry))
                {
                    // Reserve the largest counters and a continuation before consuming any matching row.
                    var candidate = new SessionListSearchPage(records.ToImmutable().Add(entry),
                        SessionListSearchPage.MaximumScannedRecords, SessionListSearchPage.MaximumScannedRecords,
                        false, continuation);
                    if (SessionListSearchPage.GetSerializedSize(candidate) > SessionListSearchPage.MaximumResultBytes)
                    {
                        limited = true;
                        break;
                    }
                    records.Add(entry);
                }
                scanned++;
                if (entry.Metadata is null) { unnamed++; }
                next = scanned < page.Records.Length || page.Next is not null ? continuation : null;
                if (records.Count == limit)
                {
                    limited = next is not null;
                    break;
                }
            }
            var result = new SessionListSearchPage(records.ToImmutable(), scanned, unnamed, limited, next);
            token.ThrowIfCancellationRequested();
            _ = SessionListSearchPage.Serialize(result);
            return result;
        }, token);

    private static void ValidateListEntry(SessionWorkspaceEntry entry) => SessionListSearch.ValidateEntry(entry);
}
