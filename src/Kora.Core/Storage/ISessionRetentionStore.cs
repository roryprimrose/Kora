using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface ISessionRetentionStore
{
    ValueTask<SessionRetentionState> ReadRetentionAsync(HostId<SessionIdentity> session, CancellationToken token);
    ValueTask SetPerpetualAsync(HostRequest request, HostRevision generation, bool perpetual,
        Func<bool> admitted, CancellationToken token);
    ValueTask<SessionRetentionBatch> ApplyRetentionAsync(Func<bool> admitted,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask> revokeSources, CancellationToken token);
}
