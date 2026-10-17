using System.Collections.ObjectModel;

using Kora.Core.Context;
using Kora.Core.Artifacts;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Metadata-only navigation of already admitted bounded immutable catalogues.</summary>
public static class SkillCatalogueSearch
{
    public static IReadOnlyList<SkillPackageSnapshot> Bundled(
        SkillPackageCatalogue catalogue, string query, SkillInvocationFilter filter,
        string definitionDigest = "", string declaredResourceDigest = "", SkillCatalogueOrder order = SkillCatalogueOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        ValidateOptionalDigest(definitionDigest);
        ValidateOptionalDigest(declaredResourceDigest);
        return Order(catalogue.Packages.Where(package =>
            (definitionDigest.Length == 0 || string.Equals(package.DefinitionDigest, definitionDigest, StringComparison.Ordinal))
            && (declaredResourceDigest.Length == 0 || string.Equals(package.DeclaredResourceDigest, declaredResourceDigest, StringComparison.Ordinal))
            && (filter == SkillInvocationFilter.All
                || package.IsAvailableForInvocation == (filter == SkillInvocationFilter.Available))
            && (package.Manifest.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Description.Contains(query, StringComparison.OrdinalIgnoreCase))),
            order, package => package.Manifest.Name, package => package.Manifest.Id);
    }

    public static IReadOnlyList<SharedSkillSnapshot> Shared(
        SharedSkillCatalogue catalogue, string query, SkillInstructionFilter filter, string revisionDigest = "",
        SkillCatalogueOrder order = SkillCatalogueOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        ValidateOptionalDigest(revisionDigest);
        return Order(catalogue.Packages.Where(package =>
            (revisionDigest.Length == 0 || string.Equals(package.RevisionDigest, revisionDigest, StringComparison.Ordinal))
            && (filter == SkillInstructionFilter.All
                || package.IsInstructionCompatible == (filter == SkillInstructionFilter.Compatible))
            && (query.Length == 0
                || package.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || package.Name is not null && package.SourceQualifiedIdentity.Contains(query, StringComparison.OrdinalIgnoreCase)
                || !package.UnavailableReasons.Contains("invalid-required-description")
                    && package.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)),
            order, package => package.Name, package => package.SourceQualifiedIdentity);
    }

    private static ReadOnlyCollection<T> Order<T>(IEnumerable<T> candidates, SkillCatalogueOrder order,
        Func<T, string?> name, Func<T, string> identity)
    {
        if (order == SkillCatalogueOrder.Source) { return Array.AsReadOnly(candidates.ToArray()); }
        var knownFirst = candidates.OrderBy(candidate => name(candidate) is null);
        var named = order == SkillCatalogueOrder.NameAscending
            ? knownFirst.ThenBy(name, StringComparer.Ordinal)
            : knownFirst.ThenByDescending(name, StringComparer.Ordinal);
        return Array.AsReadOnly(named.ThenBy(identity, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateOptionalDigest(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 0) { ArtifactDigest.Validate(digest); }
    }
}
