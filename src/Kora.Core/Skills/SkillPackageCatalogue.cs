using System.Collections.ObjectModel;

namespace Kora.Core.Skills;

public sealed class SkillPackageCatalogue
{
    public const int MaximumResources = 32;
    public const int MaximumPackages = 8;

    public SkillPackageCatalogue(IReadOnlyList<string> manifestResourceIds, IReadOnlyList<SkillResourceSnapshot> resources)
    {
        ArgumentNullException.ThrowIfNull(manifestResourceIds);
        ArgumentNullException.ThrowIfNull(resources);
        if (manifestResourceIds.Count is 0 or > MaximumPackages || resources.Count is 0 or > MaximumResources
            || resources.Any(file => file is null)
            || manifestResourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifestResourceIds.Count
            || resources.Select(file => file.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resources.Count
            || resources.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resources.Count)
        {
            throw new InvalidDataException("The catalogue is bounded and rejects duplicate or aliased resource identities.");
        }
        var map = resources.ToDictionary(file => file.ResourceId, StringComparer.Ordinal);
        var packages = new List<SkillPackageSnapshot>();
        foreach (var resourceId in manifestResourceIds)
        {
            if (!map.TryGetValue(resourceId, out var manifest) || !manifest.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Every catalogue manifest must have an exact registered JSON resource.");
            }
            packages.Add(new(manifest, map));
        }
        if (packages.Select(package => package.Manifest.Id).Distinct(StringComparer.Ordinal).Count() != packages.Count
            || packages.Select(package => package.Manifest.Action).Distinct(StringComparer.Ordinal).Count() != packages.Count
            || !packages.SelectMany(package => package.Files).Select(file => file.ResourceId)
                .ToHashSet(StringComparer.Ordinal).SetEquals(map.Keys))
        {
            throw new InvalidDataException("The catalogue contains duplicate skills/actions or undeclared resources.");
        }
        Packages = packages.AsReadOnly();
    }

    public ReadOnlyCollection<SkillPackageSnapshot> Packages { get; }

    public IReadOnlyList<SkillPackageSnapshot> Dependents(string resourceId) =>
        Packages.Where(package => package.Files.Any(file =>
            string.Equals(file.ResourceId, resourceId, StringComparison.Ordinal))).ToList().AsReadOnly();
}
