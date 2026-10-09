namespace Kora.Core.Context;

public sealed class LocalFolderMetadata
{
    public LocalFolderMetadata(string canonicalPath, string directoryIdentity, IEnumerable<LocalFileMetadata> files)
    {
        Files = LocalFolderPolicy.Validate(canonicalPath, directoryIdentity, files);
        CanonicalPath = canonicalPath;
        DirectoryIdentity = directoryIdentity;
    }

    public string CanonicalPath { get; }
    public string DirectoryIdentity { get; }
    public IReadOnlyList<LocalFileMetadata> Files { get; }
    public long CombinedBytes => Files.Sum(file => file.ByteLength);
}
