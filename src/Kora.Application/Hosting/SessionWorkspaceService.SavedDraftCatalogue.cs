using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISavedDraftCatalogueStore SavedDraftCatalogue => store as ISavedDraftCatalogueStore
        ?? throw new InvalidOperationException("The private saved composer metadata source is unavailable.");

    public Task<SavedDraftPage> ReadSavedDraftCatalogue(SessionListFilter filter, string? cursor,
        Func<bool> admitted, CancellationToken token, SessionListSearch? query = null)
    {
        RequireNativeFileInput();
        if (query is not null && query.Filter != filter)
        { throw new InvalidDataException("Saved draft query and lifecycle filter differ."); }
        return ReadAsync(async () =>
        {
            var result = await SavedDraftCatalogue.ReadSavedDraftPage(filter, cursor, access.ControlRevision,
                admitted, token, query).ConfigureAwait(false);
            result.Validate();
            token.ThrowIfCancellationRequested();
            if (result.Filter != filter || result.QueryKind != query?.Kind
                || !string.Equals(result.QueryDigest, query?.Digest, StringComparison.Ordinal)
                || query is not null && result.Records.Any(row => !query.Matches(new(row.Session, row.Metadata)))
                || !admitted())
            { throw new InvalidOperationException("Saved draft metadata admission or filter changed. Refresh."); }
            return result;
        }, token, retentionStatusOnly: true);
    }

    public Task<SessionWorkspaceEntry> ResolveSavedDraft(SavedDraftSummary exact,
        Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        ArgumentNullException.ThrowIfNull(exact);
        exact.Validate();
        return ReadAsync(async () =>
        {
            if (access.ControlRevision != exact.ControlRevision || !admitted())
            { throw new InvalidOperationException("The exact private saved draft selection changed. Refresh."); }
            var target = await SavedDraftCatalogue.ResolveSavedDraft(exact, admitted, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (target.Authority != exact.Session || target.Metadata != exact.Metadata || !admitted()
                || access.ControlRevision != exact.ControlRevision)
            { throw new InvalidOperationException("The exact saved draft source changed. Refresh; no retargeting."); }
            return target;
        }, token, retentionStatusOnly: true);
    }
}
