using System.Collections.Immutable;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed record SessionMemoryContextSnapshot(
    MemoryBoundary Boundary, ImmutableArray<MemoryRecord> Records);
