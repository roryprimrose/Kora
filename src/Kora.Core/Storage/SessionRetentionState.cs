using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionRetentionState(
    HostId<SessionIdentity> SessionId, DateTimeOffset LastMeaningfulActivity,
    DateTimeOffset ArchiveDue, DateTimeOffset DeleteDue, bool Perpetual, bool Purged);
