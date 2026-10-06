using System.Runtime.InteropServices;
using Kora.Core.Hosting;

namespace Kora.Core.Presentation;

[StructLayout(LayoutKind.Auto)]
public readonly record struct DetailContentReference
{
    public DetailContentReference(HostId<EvidenceIdentity> itemId, long revision)
    {
        itemId.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ItemId = itemId;
        Revision = revision;
    }

    public HostId<EvidenceIdentity> ItemId { get; }
    public long Revision { get; }

    public void Validate() => ItemId.Validate();
}
