using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>One passive, bounded authority snapshot; never an execution or activity lease.</summary>
public interface ISessionWorkStore
{
    ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session,
        long admissionRevision, SessionQueueLimits limits, CancellationToken token);

    /// <summary>Re-read under the owning storage lease and hold it across a content-free local observation commit.</summary>
    ValueTask<T> WithCurrentWorkAsync<T>(HostId<SessionIdentity> session,
        long admissionRevision, SessionQueueLimits limits, Func<SessionWorkSnapshot, T> observation,
        CancellationToken token) => throw new InvalidOperationException("Atomic local work observation is unavailable.");
}
