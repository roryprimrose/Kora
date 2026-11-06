using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.Conversation;

/// <summary>Content-free exact same-session memory revision handed to the local adapter.</summary>
public sealed record SubmittedMemorySource(HostId<MemoryIdentity> Id, HostRevision Revision,
    HostId<SessionIdentity> Session)
{
    public void Validate()
    {
        Id.Validate();
        Session.Validate();
        if (Revision.Value <= 0)
        { throw new InvalidDataException("A submitted memory reference requires an exact positive revision."); }
    }
}
