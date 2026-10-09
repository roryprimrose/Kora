using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>One passive, bounded authority snapshot; never an execution or activity lease.</summary>
public interface ISessionWorkStore
{
    ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session,
        long admissionRevision, SessionQueueLimits limits, CancellationToken token);
}
