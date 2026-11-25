using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using IMcpProcessPreparation = Kora.Core.Mcp.IMcpConnectionBroker.IMcpProcessPreparation;

namespace Kora.Windows.Mcp;

/// <summary>Prepares held Windows identities without starting a process or treating a read grant as launch authority.</summary>
public sealed class WindowsMcpLaunchAuthority(IApplicationDataPaths paths)
{
    private readonly Lock gate = new();
    private readonly HashSet<IMcpProcessPreparation> owned = [];

    internal async Task<IMcpProcessPreparation> PrepareAsync(McpConnectionIdentity identity, TimeProvider time, CancellationToken token)
    {
        var preparation = await WindowsMcpProcessLease.PrepareAsync(identity, paths, time, token,
            released => { lock (gate) { owned.Remove(released); } }).ConfigureAwait(false);
        lock (gate) { owned.Add(preparation); }
        return preparation;
    }
}
