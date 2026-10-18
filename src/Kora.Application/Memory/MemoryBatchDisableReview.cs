using System.Collections.Immutable;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed class MemoryBatchDisableReview(
    HostId<SessionIdentity> session, ImmutableArray<MemoryRecord> records)
{
    internal HostId<SessionIdentity> Session { get; } = session;
    internal ImmutableArray<MemoryRecord> Records { get; } = records;
}
