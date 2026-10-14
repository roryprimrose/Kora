using Kora.Core.Context;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Metadata-only navigation of already admitted bounded immutable catalogues.</summary>
public static class SkillCatalogueSearch
{
    public static IReadOnlyList<SkillPackageSnapshot> Bundled(
        SkillPackageCatalogue catalogue, string query, SkillInvocationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        return Array.AsReadOnly(catalogue.Packages.Where(package =>
            (filter == SkillInvocationFilter.All
                || package.IsAvailableForInvocation == (filter == SkillInvocationFilter.Available))
            && (package.Manifest.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Description.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray());
    }

    public static IReadOnlyList<SharedSkillSnapshot> Shared(
        SharedSkillCatalogue catalogue, string query, SkillInstructionFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        return Array.AsReadOnly(catalogue.Packages.Where(package =>
            (filter == SkillInstructionFilter.All
                || package.IsInstructionCompatible == (filter == SkillInstructionFilter.Compatible))
            && (query.Length == 0
                || package.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || package.Name is not null && package.SourceQualifiedIdentity.Contains(query, StringComparison.OrdinalIgnoreCase)
                || !package.UnavailableReasons.Contains("invalid-required-description")
                    && package.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)).ToArray());
    }
}
