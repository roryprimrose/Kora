using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

public interface ISessionWorkspaceStore
{
    ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken);
    ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration,
        bool active, Func<bool> canControl, CancellationToken cancellationToken);
}
