using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private readonly Lock dispositionGate = new();
    private PendingDisposition? pendingDisposition;
    private long dispositionPreviewEpoch;

    public bool IsDispositionPreviewCurrent(SessionDispositionPreview preview)
    {
        lock (dispositionGate)
        {
            return pendingDisposition is { } pending && pending.Preview == preview
                && access.CanInspect && access.CanControl && access.ControlRevision == pending.ControlRevision;
        }
    }

    public void DiscardDispositionPreview(SessionDispositionPreview preview)
    {
        lock (dispositionGate)
        {
            if (pendingDisposition?.Preview != preview) { return; }
            pendingDisposition = null;
            dispositionPreviewEpoch++;
        }
    }

    public async Task<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
        HostRevision generation, long metadataRevision, CancellationToken token)
    {
        var revision = access.ControlRevision;
        long epoch;
        lock (dispositionGate)
        {
            pendingDisposition = null;
            epoch = ++dispositionPreviewEpoch;
        }
        var preview = await ReadAsync(() => store.PreviewDispositionAsync(session, generation, metadataRevision, token), token)
            .ConfigureAwait(false);
        if (!access.CanControl || access.ControlRevision != revision)
        {
            throw new InvalidOperationException("Disposition preview requires unchanged private host control admission.");
        }
        token.ThrowIfCancellationRequested();
        lock (dispositionGate)
        {
            if (epoch != dispositionPreviewEpoch)
            { throw new InvalidOperationException("Disposition preview was replaced. Request a fresh exact preview."); }
            pendingDisposition = new(preview, revision);
        }
        return preview;
    }

    public async Task<SessionDispositionReceipt> ConfirmDispositionAsync(SessionDispositionPreview preview,
        RequestOrigin origin, Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(admission);
        PendingDisposition? pending;
        lock (dispositionGate)
        {
            pending = pendingDisposition;
            pendingDisposition = null;
        }
        // This increment is native UI only. No voice/model or direct arbitrary proposal confirmation.
        if (origin != RequestOrigin.LocalUi || preview.StoppedWork is not null || pending is null || pending.Preview != preview
            || !access.CanControl || access.ControlRevision != pending.ControlRevision || !admission())
        {
            throw new InvalidOperationException("No matching live native disposition preview. Preview again before explicit confirmation.");
        }
        return await CommitDispositionAsync(preview, origin,
            () => access.ControlRevision == pending.ControlRevision && admission(), token).ConfigureAwait(false);
    }

    internal Task<SessionDispositionReceipt> CommitStoppedDispositionAsync(SessionDispositionPreview preview,
        RequestOrigin origin, Func<bool> physicallyJoinedAndAdmitted, CancellationToken token)
    {
        if (origin != RequestOrigin.LocalUi || preview.StoppedWork is null)
        { throw new InvalidOperationException("Compound disposition requires original native deletion authority and exact stopped work."); }
        return CommitDispositionAsync(preview, origin, physicallyJoinedAndAdmitted, token);
    }

    private async Task<SessionDispositionReceipt> CommitDispositionAsync(SessionDispositionPreview preview,
        RequestOrigin origin, Func<bool> admission, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admission()) { throw new InvalidOperationException("Exact disposition authority changed before source revocation."); }
        if (attachmentLifecycle is not null)
        {
            await attachmentLifecycle.RevokeForDisposition(preview.Session.Authority.SessionId).ConfigureAwait(false);
        }
        var receipt = await ControlAsync(preview.Session.Authority.SessionId, origin,
            (request, eligible) => store.DisposeSessionAsync(request, preview, eligible, token), token,
            admission, existingSubject: true, terminalCommitted: true).ConfigureAwait(false);
        InvalidateWorkOverview(receipt.SessionId);
        SessionRetired?.Invoke(receipt.SessionId);
        if (localEvents is not null) { await localEvents.RetireSessionAsync(receipt.SessionId, token).ConfigureAwait(false); }
        return receipt;
    }

    private sealed record PendingDisposition(SessionDispositionPreview Preview, long ControlRevision);
}
