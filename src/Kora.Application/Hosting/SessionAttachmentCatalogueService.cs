using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Passive catalogue and exact navigation preparation; never persistence, resume or source consent.</summary>
public sealed class SessionAttachmentCatalogueService(SessionWorkspaceService sessions, SessionFileAttachmentService files)
{
    public async Task<SessionAttachmentPage> Read(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(admitted);
        SessionWorkspaceService.RequireNativeFileInput();
        var result = await sessions.ReadAttachmentCatalogue(query, cursor, admitted, token).ConfigureAwait(false);
        result.Validate();
        if (result.Query != query) { throw new InvalidDataException("The attachment page does not match the original native metadata query."); }
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("The private attachment catalogue was revoked before disclosure."); }
        return result;
    }

    public async Task<SessionWorkspaceEntry> ResolveExact(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentNullException.ThrowIfNull(admitted);
        SessionWorkspaceService.RequireNativeFileInput();
        await sessions.VerifyAttachmentSummary(exact, admitted, token).ConfigureAwait(false);
        var target = await sessions.ReadAttachmentTarget(exact.Session, token).ConfigureAwait(false);
        if (target.Authority.SessionId != exact.Session || target.Authority.Generation != exact.SessionGeneration
            || target.Authority.IsActive != (exact.SessionState == AttachmentSessionState.Active))
        {
            throw new InvalidOperationException("The selected exact session lineage changed. Refresh; no resume or retarget is admitted.");
        }
        var attachment = await files.Read(exact.Session, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected retained attachment is unavailable. Refresh.");
        exact.RequireAttachment(attachment);
        await sessions.VerifyAttachmentSummary(exact, admitted, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Exact attachment navigation was revoked before disclosure."); }
        return target;
    }
}
