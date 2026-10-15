namespace Kora.Core.Context;

public sealed record LocalFileReference(Guid SourceId, Guid RevisionId, Guid ItemId, string Digest)
{
    public void Validate()
    {
        if (SourceId == Guid.Empty || RevisionId == Guid.Empty || ItemId == Guid.Empty)
        { throw new InvalidDataException("A file reference requires exact nonempty source, revision and item IDs."); }
        LocalFileDigest.Validate(Digest);
    }
}
