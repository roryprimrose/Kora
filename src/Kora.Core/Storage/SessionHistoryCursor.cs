using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionHistoryCursor(
    HostId<SessionIdentity> SessionId, HostRevision Generation, long Snapshot, long After)
{
    public void Validate(HostId<SessionIdentity> session)
    {
        session.Validate();
        if (SessionId != session || Generation.Value <= 0 || Snapshot < 0 || After < 0 || After > Snapshot)
        {
            throw new ArgumentException("History cursor must own the exact session and a bounded snapshot.", nameof(session));
        }
    }
}
