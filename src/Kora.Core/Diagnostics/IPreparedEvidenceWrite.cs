using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

/// <summary>An owned, call-time-admitted ordinary snapshot; never audit authority.</summary>
public interface IPreparedEvidenceWrite
{
    HostId<EvidenceIdentity> EvidenceId { get; }
    void Write();
}
