using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Kora.Core.Context;

namespace Kora.KnowledgeSources;

internal sealed class KnowledgeSourceFolderPicker(Window owner) : IUserFolderPicker
{
    public async Task<string?> SelectFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.StorageProvider.CanPickFolder)
        { throw new InvalidOperationException("Private native folder selection is unavailable."); }
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Select one bounded named text/Markdown root", AllowMultiple = false,
        });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (folders.Count == 0) { return null; }
            if (folders.Count != 1) { throw new InvalidDataException("Select exactly one local folder."); }
            return folders[0].TryGetLocalPath()
                ?? throw new InvalidDataException("Only a local fixed-drive folder is supported.");
        }
        finally { foreach (var folder in folders) { folder.Dispose(); } }
    }
}
