using System.Collections.ObjectModel;

namespace Kora.Core.Customizations;

public sealed class CustomizationSnapshot
{
    public const int MaximumFiles = 32;
    public const int MaximumTotalBytes = 1024 * 1024;

    public CustomizationSnapshot(CustomizationSource source, string revision, IReadOnlyList<CustomizationFile> files,
        string? recoveryDetails = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        Source = source;
        Revision = revision;
        Files = ValidateFiles(source, files);
        RecoveryDetails = recoveryDetails;
    }

    public CustomizationSource Source { get; }
    public string Revision { get; }
    public ReadOnlyCollection<CustomizationFile> Files { get; }
    public string? RecoveryDetails { get; }

    public static ReadOnlyCollection<CustomizationFile> ValidateFiles(CustomizationSource source, IReadOnlyList<CustomizationFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count is 0 or > MaximumFiles || files.Any(file => file is null)
            || files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
            || files.Sum(file => file.CopyBytes().Length) > MaximumTotalBytes
            || !files.Any(file => string.Equals(file.Name, source.PrimaryFile, StringComparison.Ordinal))
            || source.Kind != CustomizationKind.Skill && files.Count != 1
            || files.Any(file => files.Any(other => other.Name.StartsWith(file.Name + '\\', StringComparison.OrdinalIgnoreCase))))
        { throw new InvalidDataException("A complete bounded file set with its exact primary file is required; only skills support companion files."); }
        return Array.AsReadOnly(files.OrderBy(file => file.Name, StringComparer.Ordinal).ToArray());
    }
}
