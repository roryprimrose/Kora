using Kora.Core.Context;

namespace Kora.Application.Presentation;

/// <summary>A one-use synchronous handoff to the exact native snapshot; no Find executor authority.</summary>
public sealed class PreparedClipboardFindEventArgs : EventArgs, IDisposable
{
    private string? query;
    private readonly Func<bool> isCurrent;

    internal PreparedClipboardFindEventArgs(ClipboardSnapshot snapshot, string query, Func<bool> isCurrent)
    {
        Snapshot = snapshot;
        this.query = query;
        this.isCurrent = isCurrent;
    }

    public ClipboardSnapshot Snapshot { get; }
    public bool IsCurrent => query is not null && isCurrent();
    public bool IsPrepared { get; private set; }

    public bool TryPrepare(Func<string, Func<bool>, bool> prepare)
    {
        if (!IsCurrent) { Dispose(); return false; }
        var literal = query!;
        query = null;
        IsPrepared = prepare(literal, isCurrent) && isCurrent();
        return IsPrepared;
    }

    public void Dispose() => query = null;
}
