namespace Kora.Core.Context;

public interface ILocalFileInspector
{
    Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken);

    /// <summary>Inspects every immediate item without reading content; rejects subdirectories and partial inventories.</summary>
    Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken) =>
        Task.FromException<ILocalFolderSelection>(new InvalidOperationException("Reviewed folder inspection is unavailable."));
}
