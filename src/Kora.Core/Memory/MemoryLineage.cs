using Kora.Core.Hosting;

namespace Kora.Core.Memory;

public sealed record MemoryLineage(
    HostRequest Request, HostRevision SessionGeneration, HostId<DeviceProfileIdentity> Profile,
    MemoryProposalOrigin Origin, HostId<MemorySourceIdentity>? Source, HostRevision? SourceRevision)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MemorySessionCopySource? CopiedFromSession { get; init; }
}
