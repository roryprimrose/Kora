using Kora.Core.Context;

namespace Kora.Application.Presentation;

/// <summary>A one-use, synchronous native preparation handoff. It grants no executor authority.</summary>
public sealed class PreparedLocalSearchEventArgs : EventArgs, IDisposable
{
    private string? query;
    private readonly Func<bool> isCurrent;

    internal PreparedLocalSearchEventArgs(LocalFileReference? file, LocalFolderReference? folder,
        string query, Func<bool> isCurrent)
    {
        File = file;
        Folder = folder;
        this.query = query;
        this.isCurrent = isCurrent;
    }

    public LocalFileReference? File { get; }
    public LocalFolderReference? Folder { get; }
    public bool IsCurrent => query is not null && isCurrent();
    public bool IsPrepared { get; private set; }

    public bool TryPrepare(Func<string, bool> prepare)
    {
        if (!IsCurrent) { Dispose(); return false; }
        var literal = query!;
        query = null;
        IsPrepared = prepare(literal) && isCurrent();
        return IsPrepared;
    }

    public void Dispose() => query = null;
}
