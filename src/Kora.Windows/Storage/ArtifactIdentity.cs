using Kora.Core.Hosting;

namespace Kora.Windows.Storage;

internal sealed record ArtifactIdentity(
    HostId<SessionIdentity> SessionId, Guid ArtifactId, HostId<SessionIdentity> DeletionOwnerId, ArtifactRole Role)
{
    internal void Validate()
    {
        SessionId.Validate();
        DeletionOwnerId.Validate();
        if (ArtifactId == Guid.Empty || !Enum.IsDefined(Role))
        {
            throw new InvalidDataException("An artifact requires valid session, artifact, deletion-owner and role identities.");
        }
    }
}
