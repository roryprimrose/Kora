using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private readonly Lock dispositionGate = new();
    private PendingDisposition? pendingDisposition;

    public async Task<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
        HostRevision generation, long metadataRevision, CancellationToken token)
    {
        var revision = access.ControlRevision;
        lock (dispositionGate) { pendingDisposition = null; }
        var preview = await ReadAsync(() => store.PreviewDispositionAsync(session, generation, metadataRevision, token), token)
            .ConfigureAwait(false);
        if (!access.CanControl || access.ControlRevision != revision)
        {
            throw new InvalidOperationException("Disposition preview requires unchanged private host control admission.");
        }
        lock (dispositionGate) { pendingDisposition = new(preview, revision); }
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
        if (origin != RequestOrigin.LocalUi || pending is null || pending.Preview != preview
            || !access.CanControl || access.ControlRevision != pending.ControlRevision || !admission())
        {
            throw new InvalidOperationException("No matching live native disposition preview. Preview again before explicit confirmation.");
        }
        var receipt = await ControlAsync(preview.Session.Authority.SessionId, origin,
            (request, eligible) => store.DisposeSessionAsync(request, preview, eligible, token), token,
            () => access.ControlRevision == pending.ControlRevision && admission(), existingSubject: true, terminalCommitted: true).ConfigureAwait(false);
        SessionRetired?.Invoke(receipt.SessionId);
        if (localEvents is not null) { await localEvents.RetireSessionAsync(receipt.SessionId, token).ConfigureAwait(false); }
        return receipt;
    }

    private sealed record PendingDisposition(SessionDispositionPreview Preview, long ControlRevision);
}
