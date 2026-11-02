using Kora.Core.Hosting;

namespace Kora.Core.Memory;

/// <summary>Content-free provenance, not continuing authority over an independently admitted profile memory.</summary>
public sealed record MemorySessionCopySource(
    HostId<MemoryIdentity> Id, HostRevision Revision, HostId<SessionIdentity> Session, HostRevision Generation);
