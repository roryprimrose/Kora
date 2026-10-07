using Kora.Core.Hosting;

namespace Kora.Core.Storage;

public sealed record SessionMetadata(HostId<SessionIdentity> SessionId, HostRevision Revision, SessionName Name);
