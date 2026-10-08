using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public interface ISharedSkillSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateSharedSkillSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);
    ValueTask<T> WithSharedSkillSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken);
}
