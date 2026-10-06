using Kora.Core.Hosting;

namespace Kora.Core.Presentation;

public sealed record DetailSessionSource(
    HostId<SessionIdentity> SessionId,
    HostId<RequestIdentity> RequestId,
    HostId<TaskIdentity> TaskId)
{
    public void Validate()
    {
        SessionId.Validate();
        RequestId.Validate();
        TaskId.Validate();
    }
}
