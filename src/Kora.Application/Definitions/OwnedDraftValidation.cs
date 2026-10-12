using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class OwnedDraftValidation
{
    internal OwnedDraftValidation(string name, ArtifactKind kind, bool bom, ArtifactFileDocument? proposed,
        string? failure, IReadOnlyList<ArtifactDefinition> candidates, long failureRevision, bool unavailable)
    {
        Name = name;
        Kind = kind;
        HasBom = bom;
        ProposedByteCount = proposed?.ByteCount;
        Digest = proposed?.Definition.Digest;
        NativeFormat = proposed?.Metadata.ContainsKey("kora-format");
        Failure = failure;
        CatalogueIdentity = candidates;
        FailureRevision = failureRevision;
        SourceUnavailable = unavailable;
    }

    public string Name { get; }
    public ArtifactKind Kind { get; }
    public bool HasBom { get; }
    public int? ProposedByteCount { get; }
    public string? Digest { get; }
    public bool? NativeFormat { get; }
    public string? Failure { get; }
    public bool SourceUnavailable { get; }
    public bool Supported => Failure is null;
    internal IReadOnlyList<ArtifactDefinition> CatalogueIdentity { get; }
    internal long FailureRevision { get; }

    public string Summary =>
        (Supported ? "Supported structural draft observed; nothing saved/enabled; Save separately revalidates and requires exact confirmation. "
            : "Prepared Save proposal rejected: " + Failure + " Nothing saved/enabled. ")
        + $"Historical observation only. Owned {Kind}; ID {Name}; source: Kora-owned; "
        + $"model context role: {(Kind == ArtifactKind.Prompt ? "literal user request" : "instruction guidance")}; review state: not approved. "
        + (Supported ? $"Prepared bytes including BOM/header: {ProposedByteCount}; BOM={HasBom}; "
            + $"format: {(NativeFormat == true ? "native v1" : "unchanged compatible legacy")}; SHA-256: {Digest}. " : string.Empty)
        + "Preparation includes any marker Save would add, not just raw draft bytes. "
        + (SourceUnavailable ? "Source/catalogue unavailable: Reload/reconciliation is still required before Save or use. " : string.Empty)
        + "This is not semantic harmlessness, safe execution, permission, activation or current disk integrity.";
}
