using Kora.Core.Artifacts;
using Kora.Core.Skills;

namespace Kora.Core.Customizations;

public sealed class DeclarativeSkillAdaptation
{
    private DeclarativeSkillAdaptation(CustomizationSnapshot original, CustomizationSource destination,
        string attribution, CustomizationFile proposed)
    {
        Original = original;
        Destination = destination;
        Attribution = attribution;
        Proposed = proposed;
    }

    public CustomizationSnapshot Original { get; }
    public CustomizationSource Destination { get; }
    public string Attribution { get; }
    public CustomizationFile Proposed { get; }

    public static DeclarativeSkillAdaptation Create(CustomizationSnapshot original, CustomizationSource destination)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(destination);
        if (original.Source.Kind != CustomizationKind.Skill || original.Source.Scope != CustomizationScope.User
            || original.RecoveryDetails is not null || original.Files.Count != 1
            || destination.Kind != CustomizationKind.Skill || destination.Scope != CustomizationScope.Kora || destination.IsReadOnly
            || original.Source.Name.Equals(destination.Name, StringComparison.OrdinalIgnoreCase)
            || destination.PrimaryFile is not "SKILL.md")
        { throw new InvalidDataException("Adaptation requires one inspected compatible shared declarative SKILL.md and a distinct new Kora ID. Bundled action packages, companions and recovery are not flattened."); }
        OwnedDeclarativeSkillDocument.RequireIdentity(destination.Name, "1.0.0");
        var primary = original.Files.Single();
        if (primary.Name is not "SKILL.md")
        { throw new InvalidDataException("Only the exact canonical shared SKILL.md can be adapted."); }
        var compatible = RequirePlain(primary.CopyBytes());
        if (!string.Equals(compatible.Name, original.Source.Name, StringComparison.Ordinal))
        { throw new InvalidDataException("The declared source name must match its exact inspected folder."); }
        var document = ArtifactFileDocument.Parse(primary.CopyBytes(), ArtifactKind.Skill, "user-profile", original.Source.Name);
        var attribution = $"Kora adaptation of {compatible.Name}@{compatible.Version}; source {original.Source.Id}; location {original.Source.Location}; revision {original.Revision}; reader {SharedSkillSnapshot.ReaderVersion}";
        if (attribution.Any(char.IsControl))
        { throw new InvalidDataException("Source lineage cannot be represented as one inert attribution scalar."); }
        var text = $"---\nname: {destination.Name}\nversion: {compatible.Version}\ndescription: {compatible.Description}\nattribution: {attribution}\n---\n"
            + document.Body;
        return new(original, destination, attribution,
            new("SKILL.md", ArtifactFileDocument.Encode(text, primary.HasBom)));
    }

    public ArtifactFileDocument ValidateDraft(IReadOnlyList<CustomizationFile> files)
    {
        if (files.Count != 1 || files[0].Name is not "SKILL.md")
        { throw new InvalidDataException("This adaptation supports only one complete declarative SKILL.md; dependencies cannot be removed or flattened."); }
        var document = ArtifactFileDocument.Parse(files[0].CopyBytes(), ArtifactKind.Skill, "kora-profile", Destination.Name);
        if (document.Metadata.Count != 4
            || !string.Equals(document.Metadata.GetValueOrDefault("attribution"), Attribution, StringComparison.Ordinal)
            || !string.Equals(document.Metadata.GetValueOrDefault("name"), Destination.Name, StringComparison.Ordinal))
        { throw new InvalidDataException("Keep the exact destination name and source attribution. Unknown metadata and dependency declarations are not supported."); }
        var validation = $"---\nname: {Destination.Name}\nversion: {document.Metadata.GetValueOrDefault("version")}\ndescription: {document.Metadata.GetValueOrDefault("description")}\n---\n"
            + document.Body;
        RequirePlain(ArtifactFileDocument.Encode(validation, false));
        return document;
    }

    private static SharedSkillSnapshot RequirePlain(byte[] bytes)
    {
        var snapshot = SharedSkillSnapshot.Parse(Guid.NewGuid(), "SKILL.md", bytes, false);
        if (!snapshot.IsInstructionCompatible || snapshot.ReferenceDeclaration is not null)
        { throw new InvalidDataException("Skill adaptation refused: " + string.Join(", ", snapshot.UnavailableReasons)
            + ". Only the v1 name/version/description and plain inert guidance subset is supported; required dependencies are never dropped."); }
        return snapshot;
    }
}
