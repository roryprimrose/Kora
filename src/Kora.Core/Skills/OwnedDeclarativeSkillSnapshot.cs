namespace Kora.Core.Skills;

public sealed record OwnedDeclarativeSkillSnapshot(
    OwnedDeclarativeSkillDocument Document, string DirectoryIdentity, string ManifestIdentity, string InstructionsIdentity)
{
    public bool IsSameRevision(OwnedDeclarativeSkillSnapshot other) =>
        string.Equals(DirectoryIdentity, other.DirectoryIdentity, StringComparison.Ordinal)
        && string.Equals(ManifestIdentity, other.ManifestIdentity, StringComparison.Ordinal)
        && string.Equals(InstructionsIdentity, other.InstructionsIdentity, StringComparison.Ordinal)
        && Document.ManifestBytes.Span.SequenceEqual(other.Document.ManifestBytes.Span)
        && Document.InstructionBytes.Span.SequenceEqual(other.Document.InstructionBytes.Span);

    public override string ToString() => $"{Document.Name} ({Document.Id} {Document.Version}) - saved, inactive";
}
