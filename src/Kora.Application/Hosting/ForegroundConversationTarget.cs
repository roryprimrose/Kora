using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One host-published presenter snapshot with independent focus and resource lifetimes.</summary>
public sealed class ForegroundConversationTarget(SessionWorkspaceEntry entry, Func<bool> eligible)
{
    private int retired;
    private int foregroundReleased;
    public SessionWorkspaceEntry Entry { get; } = entry;
    public bool IsCurrent => Volatile.Read(ref foregroundReleased) == 0 && IsResourceCurrent;
    public bool IsResourceCurrent => Volatile.Read(ref retired) == 0 && eligible();
    public void ReleaseForeground() => Interlocked.Exchange(ref foregroundReleased, 1);
    public void Retire() => Interlocked.Exchange(ref retired, 1);
}
