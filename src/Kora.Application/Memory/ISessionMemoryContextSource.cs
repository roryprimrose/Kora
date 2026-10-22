using System.Collections.Immutable;
using Kora.Core.Commands;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

/// <summary>Authoritative read-only admission of explicitly selected session memory.</summary>
internal interface ISessionMemoryContextSource
{
    /// <summary>Monotonic host invalidation revision, not a source content identity.</summary>
    long Revision { get; }

    /// <summary>Invalidates private reviews and active uses before memory management changes.</summary>
    event Action? Invalidated;

    /// <summary>Reads only exact enabled, reviewed, retained session records under a live private fence.</summary>
    Task<SessionMemoryContextSnapshot> ReadAsync(SessionWorkspaceEntry target,
        ImmutableArray<MemorySummary> selection, Func<bool> admitted, CancellationToken token);
}
