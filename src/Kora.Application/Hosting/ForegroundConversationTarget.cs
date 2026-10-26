using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One host-published foreground presenter snapshot, irreversibly retired on source change.</summary>
public sealed class ForegroundConversationTarget(SessionWorkspaceEntry entry, Func<bool> eligible)
{
    private int retired;
    public SessionWorkspaceEntry Entry { get; } = entry;
    public bool IsCurrent => Volatile.Read(ref retired) == 0 && eligible();
    public void Retire() => Interlocked.Exchange(ref retired, 1);
}
