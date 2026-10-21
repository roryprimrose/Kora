using System.Collections.ObjectModel;

using Kora.Core.Context;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Metadata-only navigation of already admitted bounded immutable catalogues.</summary>
public static class SkillCatalogueSearch
{
    public static IReadOnlyList<SkillPackageSnapshot> Bundled(
        SkillPackageCatalogue catalogue, string query, SkillInvocationFilter filter,
        SkillCatalogueOrder order = SkillCatalogueOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        return Order(catalogue.Packages.Where(package =>
            (filter == SkillInvocationFilter.All
                || package.IsAvailableForInvocation == (filter == SkillInvocationFilter.Available))
            && (package.Manifest.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || package.Manifest.Description.Contains(query, StringComparison.OrdinalIgnoreCase))),
            order, package => package.Manifest.Name, package => package.Manifest.Id);
    }

    public static IReadOnlyList<SharedSkillSnapshot> Shared(
        SharedSkillCatalogue catalogue, string query, SkillInstructionFilter filter,
        SkillCatalogueOrder order = SkillCatalogueOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        LiteralMetadataQuery.Validate(query);
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        return Order(catalogue.Packages.Where(package =>
            (filter == SkillInstructionFilter.All
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

}
