using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private SessionFileAttachmentService? attachmentLifecycle;
    public void BindAttachmentLifecycle(SessionFileAttachmentService service) => attachmentLifecycle = service;

    private ISessionFileStore Files => store as ISessionFileStore
        ?? throw new InvalidOperationException("The private session file store is unavailable.");

    public Task<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => Files.ReadAttachment(session, token), token);

    internal Task<SessionWorkspaceEntry> ReadAttachmentTarget(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => store.ReadMetadataAsync(session, token), token);

    private ISessionAttachmentCatalogueStore AttachmentCatalogue => store as ISessionAttachmentCatalogueStore
        ?? throw new InvalidOperationException("The private attachment metadata catalogue is unavailable.");

    public Task<SessionAttachmentPage> ReadAttachmentCatalogue(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token) =>
        // The catalogue enforces the same passive retention/source gate without Open/ValidateAuthority,
        // which would materialize every body. Exact navigation still uses the existing full-read path.
        ReadAsync(() => AttachmentCatalogue.ReadAttachmentPage(query, cursor, admitted, token), token, retentionStatusOnly: true);

    internal Task VerifyAttachmentSummary(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            await AttachmentCatalogue.VerifyAttachmentSummary(exact, admitted, token).ConfigureAwait(false);
            return true;
        }, token, retentionStatusOnly: true);

    public Task<SessionFileRemoval> PreviewAttachmentRemoval(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => Files.PreviewRemoval(session, token), token);

    internal Task<SessionFileAttachment> CommitAttachment(HostRequest original, HostRevision generation,
        LocalFileRevision file, ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        InvalidateWorkOverview(original.SessionId);
        return ControlAsync(original.SessionId, RequestOrigin.LocalUi,
            (request, eligible) => Files.Attach(request, generation, file, bytes, eligible, token),
            token, admitted, existingSubject: true, resolveTerminalReceipt: true, originalTask: original.TaskId);
    }

    public Task<bool> RemoveAttachment(SessionFileRemoval review, Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        return ControlAsync(review.Session, RequestOrigin.LocalUi, async (request, eligible) =>
        {
            await Files.Remove(request, review, eligible, token).ConfigureAwait(false);
            return true;
        }, token, admitted, existingSubject: true, resolveTerminalReceipt: true);
    }

    internal Task<SessionFileAttachment> ReplaceAttachment(HostRequest original, SessionFileRemoval previous,
        LocalFileRevision file, ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        return ControlAsync(original.SessionId, RequestOrigin.LocalUi,
            (request, eligible) => Files.Replace(request, previous, file, bytes, eligible, token),
            token, admitted, existingSubject: true, terminalCommitted: true, originalTask: original.TaskId);
    }

    internal static void RequireNativeFileInput()
    {
        var current = HostActivity.Current;
        if (HostActivity.HasScope && (current is null || current.Request.Origin != RequestOrigin.LocalUi
            || current.Outcome != HostOperationOutcome.Unknown || current.Request.InvocationId is not null))
        {
            throw new InvalidOperationException("Session attachment controls require fresh original native user input.");
        }
    }
}
