using System.Collections.Immutable;
using Kora.Core.Hosting;

namespace Kora.Core.Memory;

/// <summary>Host-owned session memory data and required audit share the existing authority transaction.</summary>
public interface IMemoryStore
{
    /// <summary>Resolves the private store and exact active session; never resolves broader scopes.</summary>
    ValueTask<MemoryBoundary> ReadMemoryBoundaryAsync(HostRequest request, long controlRevision, CancellationToken token);

    /// <summary>Runs a host transition against authoritative current rows with a final admission fence.</summary>
    ValueTask<MemoryResult> TransactMemoryAsync(HostRequest request, MemoryBoundary boundary,
        Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token);
}
