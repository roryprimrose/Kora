using System.Collections.Immutable;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed class MemoryBatchReview(
    MemoryBatchOperation operation, HostId<SessionIdentity> session, ImmutableArray<MemoryRecord> records,
    Func<bool> contentAdmission)
{
    private readonly Lock gate = new();
    private ImmutableArray<MemoryRecord> contents = records;
    internal MemoryBatchOperation Operation { get; } = operation;
    internal HostId<SessionIdentity> Session { get; } = session;
    internal ImmutableArray<MemoryRecord> Records
    {
        get
        {
            if (!contentAdmission()) { Retire(); }
            lock (gate) { return contents; }
        }
    }

    internal ImmutableArray<MemoryRecord> Consume()
    {
        lock (gate)
        {
            var result = contents;
            contents = [];
            return result;
        }
    }

    internal void Retire() { lock (gate) { contents = []; } }
}
