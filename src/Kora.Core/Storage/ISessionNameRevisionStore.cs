using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Adds a captured passive source fence to the existing revisioned rename transaction.</summary>
public interface ISessionNameRevisionStore
{
    ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
        long expectedMetadataRevision, SessionName name, long expectedSourceRevision,
        Func<bool> canControl, CancellationToken cancellationToken);
}
