using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private readonly Guid listSearchHost = Guid.NewGuid();

    public Task<SessionListSearchPage> SearchListAsync(SessionListSearchKind kind, SessionListFilter filter,
        string query, SessionListSearchCursor? cursor, int limit, CancellationToken token) =>
        ReadAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var search = new SessionListSearch(kind, filter, query);
            SessionListSearchPage.ValidateLimit(limit);
            cursor?.Validate(listSearchHost, access.ControlRevision, search);
            if (search.ExactId is { } id)
            {
                var entry = await store.ReadMetadataAsync(new(id), token).ConfigureAwait(false);
                ValidateListEntry(entry);
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
                || page.Next is not null && (page.Records.IsEmpty || page.Next == Guid.Empty
                    || page.Next != page.Records[^1].Authority.SessionId.Value))
            {
                throw new InvalidDataException("Metadata continuation requires a bounded advancing source page.");
            }
            var previous = cursor?.After.ToString("D") ?? string.Empty;
            foreach (var entry in page.Records)
            {
                token.ThrowIfCancellationRequested();
                ValidateListEntry(entry);
                var current = entry.Authority.SessionId.Value.ToString("D");
                if (string.CompareOrdinal(previous, current) >= 0)
                {
                    throw new InvalidDataException("Metadata requires strictly ordered canonical session IDs.");
                }
                previous = current;
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

    private static void ValidateListEntry(SessionWorkspaceEntry entry)
    {
        entry.Authority.SessionId.Validate();
        _ = new HostRevision(entry.Authority.Generation.Value);
        if (entry.Metadata is { } metadata)
        {
            if (metadata.SessionId != entry.Authority.SessionId)
            {
                throw new InvalidDataException("Name metadata must belong to its exact session.");
            }
            _ = new HostRevision(metadata.Revision.Value);
            _ = new SessionName(metadata.Name.Value);
        }
    }
}
