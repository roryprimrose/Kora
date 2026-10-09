namespace Kora.Core.Context;

/// <summary>Owns metadata-only reviewed folder and file handles until capture or release.</summary>
public interface ILocalFolderSelection : IDisposable
{
    /// <summary>Gets the complete immutable immediate-file inventory.</summary>
    LocalFolderMetadata Metadata { get; }
    /// <summary>Revalidates the exact inventory and retained identities without reading content.</summary>
    Task ValidateAsync(CancellationToken cancellationToken);
    /// <summary>Reads one exact reviewed item once through its retained handle.</summary>
    Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken);
}
