using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Host-owned identity for one explicit native fresh-target creation; never derived from request text.</summary>
public sealed class NewRequestCreationAttempt
{
    public HostRequest Request { get; } = HostRequest.Create(RequestOrigin.LocalUi);
    public SessionName Name { get; } = new("New conversation");

    public void RequireExactTarget(SessionWorkspaceEntry target)
    {
        if (target.Authority.SessionId != Request.SessionId || !target.Authority.IsActive
            || target.Authority.Generation.Value != 1 || target.Metadata is not { Revision.Value: 1 } metadata
            || metadata.SessionId != Request.SessionId || metadata.Name != Name)
        { throw new InvalidDataException("Fresh target creation receipt does not match its exact host-owned identity."); }
    }
}
