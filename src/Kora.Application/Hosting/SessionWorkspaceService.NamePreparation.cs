using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    /// <summary>Passive exact observation; the maintenance/source epoch is not rename authority.</summary>
    public Task<(SessionWorkspaceEntry Entry, long SourceRevision)> ReadNamePreparationAsync(
        HostId<SessionIdentity> session, CancellationToken token) => ReadAsync(async () =>
        {
            var source = await RetentionControls.ValidatePassiveInspectionAsync(token).ConfigureAwait(false);
            var entry = await store.ReadMetadataAsync(session, token).ConfigureAwait(false);
            SessionListSearch.ValidateEntry(entry);
            if (entry.Authority.SessionId != session
                || source != await RetentionControls.ValidatePassiveInspectionAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The exact session name source changed during inspection.");
            }
            token.ThrowIfCancellationRequested();
            return (entry, source);
        }, token);
}
