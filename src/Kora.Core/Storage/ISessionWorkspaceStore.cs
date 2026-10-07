using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

public interface ISessionWorkspaceStore
{
    /// <summary>Requires existing task authority; missing storage must never be initialized as an idle ledger.</summary>
    ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken);
    ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration,
        bool active, Func<bool> canControl, CancellationToken cancellationToken);
}
