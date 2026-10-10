using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionFileStore Files => store as ISessionFileStore
        ?? throw new InvalidOperationException("The private session file store is unavailable.");

    public Task<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => Files.ReadAttachment(session, token), token);

    public Task<SessionFileRemoval> PreviewAttachmentRemoval(HostId<SessionIdentity> session, CancellationToken token) =>
        ReadAsync(() => Files.PreviewRemoval(session, token), token);

    internal Task<SessionFileAttachment> CommitAttachment(HostRequest original, HostRevision generation,
        LocalFileRevision file, ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
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
