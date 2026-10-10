using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionFileAttachment(HostId<SessionIdentity> Session, HostRevision Generation,
    HostRevision StorageRevision, LocalFileRevision File)
{
    public const int MaximumRetainedFiles = 16;
    public const string Projection = "strict-utf8-v1";
    public const string CopyInventory = "Private interaction.db snapshot row, SQLite cell/index/free pages and owned PERSIST rollback journal. "
        + "No artifact, staging, backup, export or index copies are created. Uninventoried copies and live/Unknown work hold deletion. "
        + "Original user files are unchanged. This is logical owned-copy deletion, not forensic erasure of storage hardware.";
}
