using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Interaction;

/// <summary>Host-owned, fixed-profile authority adapter. There is no model/tool event publication route.</summary>
public interface ILocalEventSource
{
    Task<IReadOnlyList<LocalEvent>> ReadAsync(HostId<SessionIdentity> session, CancellationToken token);
    Task<T> WithCurrentAsync<T>(HostId<SessionIdentity> session, IReadOnlyList<LocalEvent> expected,
        Func<T> observation, CancellationToken token);
}
