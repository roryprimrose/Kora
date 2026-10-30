using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

public static class KnowledgeSourcePolicy
{
    public const int MaximumDirectoryDepth = 4;
    public const int MaximumDirectories = 32;
    public const int MaximumFiles = LocalFolderPolicy.MaximumFiles;
    public const int MaximumCombinedBytes = LocalFolderPolicy.MaximumCombinedBytes;
    public static readonly TimeSpan MaximumOperationTime = TimeSpan.FromSeconds(30);

    public static void ValidateScope(KnowledgeSourceScope scope)
    {
        if (!Enum.IsDefined(scope)) { throw new InvalidDataException("Unknown named-source traversal policy."); }
    }

    public static string RelativePath(string root, string path)
    {
        if (!path.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("A named-source item must remain below its exact root."); }
        return path[(root.Length + 1)..];
    }
}
