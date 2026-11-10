using Kora.Core.Artifacts;

namespace Kora.Core.Conversation;

/// <summary>Historical content-free snapshot provenance, never a source lookup or use token.</summary>
public sealed record SubmittedInstructionSource(Guid Reference, string Revision, string Digest)
{
    public void Validate()
    {
        if (Reference == Guid.Empty || Revision is null || Digest is null)
        { throw new InvalidDataException("Submitted instruction provenance is incomplete."); }
        _ = OwnedInstructionRevision.Validate(Revision);
        try { _ = ArtifactDigest.Validate(Digest); }
        catch (ArgumentException exception)
        { throw new InvalidDataException("Submitted instruction digest is invalid.", exception); }
        if (!Revision.StartsWith(Digest + ".", StringComparison.Ordinal))
        { throw new InvalidDataException("Submitted instruction provenance must bind the original snapshot digest."); }
    }
}
