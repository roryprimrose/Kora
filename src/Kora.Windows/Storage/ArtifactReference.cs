namespace Kora.Windows.Storage;

internal sealed record ArtifactReference(ArtifactIdentity Identity, Guid KeyId, int Length, string Digest)
{
    internal void Validate()
    {
        if (Identity is null)
        {
            throw new InvalidDataException("The immutable artifact identity is missing.");
        }
        Identity.Validate();
        if (KeyId == Guid.Empty || Length is < 0 or > ArtifactEnvelope.MaximumPlaintextBytes
            || Digest is null || Digest.Length != 64
            || Digest.Any(static character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new InvalidDataException("The immutable artifact reference is invalid.");
        }
    }
}
