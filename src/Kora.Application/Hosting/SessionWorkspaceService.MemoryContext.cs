using Kora.Application.Memory;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task<SessionMemoryContextSnapshot> ExecuteMemoryContextReadAsync(HostId<SessionIdentity> session,
        RequestOrigin origin, Func<bool> admitted,
        Func<HostRequest, Func<bool>, ValueTask<SessionMemoryContextSnapshot>> read, CancellationToken token) =>
        ControlAsync(session, origin, read, token, admitted, existingSubject: true,
            resolveTerminalReceipt: true, finalizeIncompleteOnFailure: true);
}
