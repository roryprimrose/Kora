using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Passive exact identity reads; absence/corruption is never an empty replacement.</summary>
public interface ISessionHistoryStore
{
    ValueTask<SessionHistoryPage> ReadHistoryAsync(HostId<SessionIdentity> session,
        SessionHistoryCursor? cursor, int limit, CancellationToken cancellationToken);
    ValueTask<SessionHistoryEvent?> ReadHistoryEventAsync(HostId<SessionIdentity> session,
        Guid eventId, CancellationToken cancellationToken);
}
