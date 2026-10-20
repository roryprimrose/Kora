using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionFileAttachmentService
{
    public async Task<SessionWorkspaceEntry> ResolveInspection(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentNullException.ThrowIfNull(admitted);
        SessionWorkspaceService.RequireRetainedFileInput();
        await sessions.VerifyAttachmentSummary(exact, admitted, token).ConfigureAwait(false);
        var target = await sessions.ReadAttachmentTarget(exact.Session, token).ConfigureAwait(false);
        if (target.Authority.SessionId != exact.Session || target.Authority.Generation != exact.SessionGeneration
            || target.Authority.IsActive != (exact.SessionState == AttachmentSessionState.Active))
        {
            throw new InvalidOperationException("The selected exact session lineage changed. Refresh; no resume or retarget is admitted.");
        }
        var attachment = await Read(exact.Session, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected retained attachment is unavailable. Refresh.");
        exact.RequireAttachment(attachment);
        await sessions.VerifyAttachmentSummary(exact, admitted, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("Exact attachment navigation was revoked before disclosure."); }
        return target;
    }
}
