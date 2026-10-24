namespace Kora.Core.Skills;

public sealed record OwnedDeclarativeSkillSnapshot(
    OwnedDeclarativeSkillDocument Document, string DirectoryIdentity, string ManifestIdentity, string InstructionsIdentity)
{
    public override string ToString() => $"{Document.Name} ({Document.Id} {Document.Version}) - saved, inactive";
}
