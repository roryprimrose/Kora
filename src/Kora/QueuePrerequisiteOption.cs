using System.Globalization;

using Kora.Core.Hosting;

namespace Kora;

internal sealed record QueuePrerequisiteOption(
    HostId<TaskIdentity> TaskId, HostId<SessionIdentity> SessionId, HostRevision Generation,
    HostRevision Revision, HostTaskState State, DateTimeOffset ObservedAt, string Source, bool CurrentSource)
{
    public string Label => "Task " + TaskId.Value.ToString("D")
        + " | session " + SessionId.Value.ToString("D")
        + " | observed generation " + Generation.Value.ToString(CultureInfo.InvariantCulture)
        + " | " + State + " | revision " + Revision.Value.ToString(CultureInfo.InvariantCulture)
        + " | observed UTC " + ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        + " | host metadata source " + Source + " | current source " + CurrentSource;
}
