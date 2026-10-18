using Kora.Core.Context;
using Kora.Core.Artifacts;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Metadata-only navigation of already admitted bounded immutable catalogues.</summary>
public static class SkillCatalogueSearch
{
    public static IReadOnlyList<SkillPackageSnapshot> Bundled(
        SkillPackageCatalogue catalogue, string query, SkillInvocationFilter filter,
        string definitionDigest = "", string declaredResourceDigest = "")
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        ValidateOptionalDigest(definitionDigest);
        ValidateOptionalDigest(declaredResourceDigest);
        return Array.AsReadOnly(catalogue.Packages.Where(package =>
            (definitionDigest.Length == 0 || string.Equals(package.DefinitionDigest, definitionDigest, StringComparison.Ordinal))
            && (declaredResourceDigest.Length == 0 || string.Equals(package.DeclaredResourceDigest, declaredResourceDigest, StringComparison.Ordinal))
            && (filter == SkillInvocationFilter.All
                || package.IsAvailableForInvocation == (filter == SkillInvocationFilter.Available))
            && (package.Manifest.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Description.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray());
    }

    public static IReadOnlyList<SharedSkillSnapshot> Shared(
        SharedSkillCatalogue catalogue, string query, SkillInstructionFilter filter, string revisionDigest = "")
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        ValidateOptionalDigest(revisionDigest);
        return Array.AsReadOnly(catalogue.Packages.Where(package =>
            (revisionDigest.Length == 0 || string.Equals(package.RevisionDigest, revisionDigest, StringComparison.Ordinal))
            && (filter == SkillInstructionFilter.All
                || package.IsInstructionCompatible == (filter == SkillInstructionFilter.Compatible))
            && (query.Length == 0
                || package.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || package.Name is not null && package.SourceQualifiedIdentity.Contains(query, StringComparison.OrdinalIgnoreCase)
                || !package.UnavailableReasons.Contains("invalid-required-description")
                    && package.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray());
    }

    private static void ValidateOptionalDigest(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 0) { ArtifactDigest.Validate(digest); }
    }
}
