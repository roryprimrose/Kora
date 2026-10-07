using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record EvidenceReference(EvidenceSource Source, HostId<EvidenceIdentity> Id, int? LinkOrdinal = null)
{
    public string Citation => $"kora-evidence:{Source.ToString().ToLowerInvariant()}:{Id.Value:D}"
        + (LinkOrdinal is { } ordinal ? $":{ordinal}" : string.Empty);
}
