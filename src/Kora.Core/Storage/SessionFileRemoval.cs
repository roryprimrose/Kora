using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionFileRemoval(Guid ConfirmationId, HostId<SessionIdentity> Session,
    HostRevision Generation, HostRevision StorageRevision, LocalFileReference File, string InventoryRevision)
{
    public bool BodyRetained { get; init; } = true;
    public bool ReplacementCopyVerificationPending { get; init; }
    public bool ReplacementSwapUnconfirmed { get; init; }
}
