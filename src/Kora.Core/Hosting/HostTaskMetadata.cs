namespace Kora.Core.Hosting;

public sealed record HostTaskMetadata(
    HostId<TaskIdentity> TaskId, HostId<SessionIdentity> SessionId, HostRevision Generation,
    HostRevision Revision, HostTaskState State, string Source, bool CurrentSource);
