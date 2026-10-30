using Kora.Core.Artifacts;

namespace Kora.Core.Customizations;

/// <summary>The common bounded deletion policy; native source ownership is independently revalidated at commit.</summary>
public static class CustomizationDeletion
{
    public static bool Supports(CustomizationSource source) =>
        source is { Scope: CustomizationScope.Kora, IsReadOnly: false,
            Kind: CustomizationKind.Instruction or CustomizationKind.Prompt or CustomizationKind.Agent };

    public static void Validate(CustomizationSnapshot reviewed)
    {
        var source = reviewed.Source;
        if (!Supports(source) || reviewed.RecoveryDetails is not null)
        { throw new InvalidDataException("Deletion requires one supported Kora-owned single-file source without pending recovery."); }
        OwnedInstructionNames.WriteName(source.Name);
        if (!string.Equals(source.PrimaryFile, CustomizationLayout.PrimaryFile(source.Kind, source.Name), StringComparison.Ordinal))
        { throw new InvalidDataException("Deletion requires the exact canonical owned filename, not an alias."); }
        var primary = reviewed.Files.Single();
        var document = ArtifactFileDocument.Parse(primary.CopyBytes(), CustomizationLayout.ArtifactKind(source.Kind),
            "kora-profile", source.Name);
        if (source.Kind is CustomizationKind.Instruction or CustomizationKind.Prompt)
        { document.RequireEditableInstruction(); }
    }
}
