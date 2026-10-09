using Kora.Core.Context;

namespace Kora.Tools.Files;

public sealed class LocalFileRefresh(LocalFilePreview preview)
{
    public Task<LocalFileOutcome> ExecuteAsync(LocalFileReference exactSource, Func<bool> canPresent, CancellationToken token) =>
        preview.RefreshAsync(exactSource, canPresent, token);

    public Task<LocalFileOutcome> ExecuteAsync(LocalFolderReference exactSource, Func<bool> canPresent, CancellationToken token) =>
        preview.RefreshAsync(exactSource, canPresent, token);
}
