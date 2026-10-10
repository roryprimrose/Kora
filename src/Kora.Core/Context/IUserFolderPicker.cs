namespace Kora.Core.Context;

/// <summary>Provides deliberate native folder selection, not text-path authority.</summary>
public interface IUserFolderPicker
{
    /// <summary>Selects one local folder for metadata-only review.</summary>
    Task<string?> SelectFolderAsync(CancellationToken cancellationToken);
}
