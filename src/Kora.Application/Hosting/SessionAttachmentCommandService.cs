using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Resolves one immutable session address into current host-held provenance, never user-supplied source authority.</summary>
public sealed class SessionAttachmentCommandService(SessionWorkspaceService sessions, SessionFileAttachmentService files,
    ISessionWorkspaceAccess access, IRetainedAttachmentViewer viewer)
{
    public async Task Execute(SessionCommand command, RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        if (command.Operation != SessionCommandOperation.AttachmentInspect || command.SessionId is not { } id || id == Guid.Empty)
        { throw new InvalidOperationException("An exact retained attachment inspection command is required."); }
        var session = new HostId<SessionIdentity>(id);
        var control = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == control && admitted() && !token.IsCancellationRequested;
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live() || !files.IsQuiescent)
            { throw new InvalidOperationException("Private inspection is unavailable. Finish or discard the current native file review/control first."); }
            var exact = await sessions.ReadAttachmentInspection(session, Live, token).ConfigureAwait(false);
            exact?.Validate();
            if (exact is null || exact.Session != session || exact.CopyStatus != AttachmentCopyStatus.Confirmed)
            { throw new InvalidOperationException("The exact retained attachment is unavailable or held. Inspect native storage/copy recovery; no original source is read."); }
            var target = await files.ResolveInspection(exact, Live, token).ConfigureAwait(false);
            if (!Live() || !files.IsQuiescent)
            { throw new InvalidOperationException("The original inspection admission changed. Initiate a fresh exact command."); }
            await viewer.Open(target, exact, Live, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Native disclosure was revoked. Initiate a fresh exact command."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
