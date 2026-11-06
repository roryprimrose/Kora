namespace Kora.Core.Skills;

public sealed record OwnedDeclarativeSkillSnapshot(
    OwnedDeclarativeSkillDocument Document, string DirectoryIdentity, string ManifestIdentity, string InstructionsIdentity)
{
    public long? StoredBytes { get; init; }
    public long TotalBytes => StoredBytes ?? Document.TotalBytes;
    public System.Collections.Immutable.ImmutableDictionary<string, string> ReferenceIdentities { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;

    public bool IsSamePrimary(OwnedDeclarativeSkillSnapshot other) =>
        string.Equals(DirectoryIdentity, other.DirectoryIdentity, StringComparison.Ordinal)
        && string.Equals(ManifestIdentity, other.ManifestIdentity, StringComparison.Ordinal)
        && string.Equals(InstructionsIdentity, other.InstructionsIdentity, StringComparison.Ordinal)
        && Document.ManifestBytes.Span.SequenceEqual(other.Document.ManifestBytes.Span)
        && Document.InstructionBytes.Span.SequenceEqual(other.Document.InstructionBytes.Span);

    public bool IsSameRevision(OwnedDeclarativeSkillSnapshot other) =>
        IsSamePrimary(other) && Document.IsComplete == other.Document.IsComplete
        && string.Equals(Document.RevisionDigest, other.Document.RevisionDigest, StringComparison.Ordinal)
        && ReferenceIdentities.Count == other.ReferenceIdentities.Count
        && ReferenceIdentities.All(item => other.ReferenceIdentities.TryGetValue(item.Key, out var identity)
            && string.Equals(item.Value, identity, StringComparison.Ordinal));

    public override string ToString() => $"{Document.Name} ({Document.Id} {Document.Version}) - saved, inactive";
}
