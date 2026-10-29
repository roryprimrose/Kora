using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

public sealed class KnowledgeSourceMetadata
{
    public KnowledgeSourceMetadata(string canonicalPath, string directoryIdentity, KnowledgeSourceScope scope,
        IEnumerable<KnowledgeSourceDirectory> directories, IEnumerable<LocalFileMetadata> files)
    {
        LocalFilePolicy.ValidateFolderPath(canonicalPath);
        KnowledgeSourcePolicy.ValidateScope(scope);
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(files);
        var nodes = new List<KnowledgeSourceDirectory>();
        var items = new List<LocalFileMetadata>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            if (directory is null) { throw new InvalidDataException("Every directory requires retained metadata."); }
            LocalFilePolicy.ValidateFolderPath(directory.CanonicalPath);
            if (string.IsNullOrWhiteSpace(directory.DirectoryIdentity) || !identities.Add(directory.DirectoryIdentity)
                || !names.Add(directory.CanonicalPath))
            { throw new InvalidDataException("Directory identities and paths must be distinct and known."); }
            if (!string.Equals(directory.CanonicalPath, canonicalPath, StringComparison.Ordinal))
            {
                var relative = KnowledgeSourcePolicy.RelativePath(canonicalPath, directory.CanonicalPath);
                if (scope != KnowledgeSourceScope.BoundedDescendants
                    || relative.Count(character => character == '\\') + 1 > KnowledgeSourcePolicy.MaximumDirectoryDepth)
                { throw new InvalidDataException("The whole source exceeds its explicitly reviewed directory scope."); }
            }
            nodes.Add(directory);
            if (nodes.Count > KnowledgeSourcePolicy.MaximumDirectories)
            { throw new InvalidDataException("The complete directory inventory exceeds its bound."); }
        }
        if (!nodes.Contains(new(canonicalPath, directoryIdentity)))
        { throw new InvalidDataException("The exact physical root must be retained in the complete inventory."); }
        foreach (var directory in nodes)
        {
            if (!string.Equals(directory.CanonicalPath, canonicalPath, StringComparison.Ordinal)
                && !names.Contains(directory.CanonicalPath[..directory.CanonicalPath.LastIndexOf('\\')]))
            { throw new InvalidDataException("Every descendant directory requires its retained parent."); }
        }
        var parents = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var file in files)
        {
            if (file is null) { throw new InvalidDataException("Every source item requires metadata."); }
            LocalFilePolicy.ValidatePath(file.CanonicalPath);
            _ = KnowledgeSourcePolicy.RelativePath(canonicalPath, file.CanonicalPath);
            if (!parents.Contains(file.CanonicalPath[..file.CanonicalPath.LastIndexOf('\\')])
                || string.IsNullOrWhiteSpace(file.FileIdentity) || !identities.Add(file.FileIdentity)
                || !names.Add(file.CanonicalPath) || file.ByteLength is < 0 or > LocalFilePolicy.MaximumBytes)
            { throw new InvalidDataException("Every file requires a distinct bounded identity and retained parent."); }
            items.Add(file);
            bytes += file.ByteLength;
            if (items.Count > KnowledgeSourcePolicy.MaximumFiles || bytes > KnowledgeSourcePolicy.MaximumCombinedBytes)
            { throw new InvalidDataException("The complete source exceeds its file or aggregate byte bound."); }
        }
        if (items.Count == 0) { throw new InvalidDataException("A named source must contain supported text files."); }
        nodes.Sort((left, right) => StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath));
        items.Sort((left, right) => StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath));
        CanonicalPath = canonicalPath;
        DirectoryIdentity = directoryIdentity;
        Scope = scope;
        Directories = nodes.AsReadOnly();
        Files = items.AsReadOnly();
    }

    public string CanonicalPath { get; }
    public string DirectoryIdentity { get; }
    public KnowledgeSourceScope Scope { get; }
    public IReadOnlyList<KnowledgeSourceDirectory> Directories { get; }
    public IReadOnlyList<LocalFileMetadata> Files { get; }
    public long CombinedBytes => Files.Sum(file => file.ByteLength);

    public bool Matches(KnowledgeSourceMetadata other) =>
        string.Equals(CanonicalPath, other.CanonicalPath, StringComparison.Ordinal)
        && string.Equals(DirectoryIdentity, other.DirectoryIdentity, StringComparison.Ordinal)
        && Scope == other.Scope && Directories.SequenceEqual(other.Directories) && Files.SequenceEqual(other.Files);

    public static KnowledgeSourceMetadata FromImmediate(LocalFolderMetadata folder) =>
        new(folder.CanonicalPath, folder.DirectoryIdentity, KnowledgeSourceScope.ImmediateFiles,
            [new(folder.CanonicalPath, folder.DirectoryIdentity)], folder.Files);
}
