using System.Collections.ObjectModel;

namespace Kora.Core.Skills;

public sealed class SharedSkillCatalogue
{
    public const int MaximumPackages = 32;
    public const int MaximumEntries = 256;
    public const int MaximumDepth = 4;
    public const int MaximumTotalBytes = 1024 * 1024;

    public SharedSkillCatalogue(SharedSkillSource source, IReadOnlyList<SharedSkillSnapshot> packages)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Count > MaximumPackages || packages.Any(package => package is null || package.SourceId != source.Id)
            || packages.Sum(package => package.Bytes.Length) > MaximumTotalBytes
            || packages.Select(package => package.RelativeFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Count)
        {
            throw new InvalidDataException("The shared source catalogue is oversized or contains aliased paths.");
        }
        var duplicateNames = packages.Where(package => package.Name is not null)
            .GroupBy(package => package.SourceQualifiedIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Packages = packages.Select(package => duplicateNames.Contains(package.SourceQualifiedIdentity)
            ? package.WithUnavailableReason("duplicate-identity") : package).ToList().AsReadOnly();
        Source = source;
    }

    public SharedSkillSource Source { get; }
    public ReadOnlyCollection<SharedSkillSnapshot> Packages { get; }
}
