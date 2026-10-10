namespace Kora.Core.Context;

public static class LocalFolderPolicy
{
    public const int MaximumFiles = 32;
    public const int MaximumCombinedBytes = 1024 * 1024;

    public static IReadOnlyList<LocalFileMetadata> Validate(string root, string identity, IEnumerable<LocalFileMetadata> files)
    {
        LocalFilePolicy.ValidateFolderPath(root);
        ArgumentNullException.ThrowIfNull(files);
        if (string.IsNullOrWhiteSpace(identity)) { throw new InvalidDataException("The folder identity is unavailable."); }
        var items = new List<LocalFileMetadata>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var item in files)
        {
            if (item is null) { throw new InvalidDataException("Every reviewed item requires metadata."); }
            LocalFilePolicy.ValidatePath(item.CanonicalPath);
            var separator = item.CanonicalPath.LastIndexOf('\\');
            if (!string.Equals(item.CanonicalPath[..separator], root, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.FileIdentity) || !identities.Add(item.FileIdentity)
                || !names.Add(item.CanonicalPath) || item.ByteLength is < 0 or > LocalFilePolicy.MaximumBytes)
            {
                throw new InvalidDataException("Every item must be a distinct bounded immediate file in the exact reviewed folder.");
            }
            bytes += item.ByteLength;
            items.Add(item);
            if (items.Count > MaximumFiles || bytes > MaximumCombinedBytes)
            {
                throw new InvalidDataException("The whole folder exceeds the file-count or combined-byte bound.");
            }
        }
        if (items.Count == 0) { throw new InvalidDataException("The selected folder must contain at least one supported immediate file."); }
        items.Sort((left, right) => StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath));
        return items.AsReadOnly();
    }
}
