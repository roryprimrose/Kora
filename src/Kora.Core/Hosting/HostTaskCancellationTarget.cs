namespace Kora.Core.Hosting;

public sealed record HostTaskCancellationTarget(
    HostId<SessionIdentity> SessionId, HostId<TaskIdentity> TaskId,
    HostRevision TaskRevision, HostRevision Generation,
    HostId<QuestionIdentity> QuestionId, HostRevision QuestionRevision);
