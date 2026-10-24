using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface IOwnedSkillControlSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(HostRequest request, Func<bool> admitted, CancellationToken token);
    ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token);
}
