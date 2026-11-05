using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.Conversation;

/// <summary>Content-free exact device-profile memory revision handed to the local adapter.</summary>
public sealed record SubmittedProfileMemorySource(HostId<MemoryIdentity> Id, HostRevision Revision,
    HostId<DeviceProfileIdentity> Profile)
{
    public void Validate()
    {
        Id.Validate();
        Profile.Validate();
        if (Revision.Value <= 0)
        { throw new InvalidDataException("A submitted profile memory reference requires an exact positive revision."); }
    }
}
