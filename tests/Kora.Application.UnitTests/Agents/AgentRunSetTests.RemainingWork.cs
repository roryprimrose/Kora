using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Agents;

public sealed partial class AgentRunSetTests
{
    internal sealed partial class Fixture : ISessionWorkStore
    {
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var target = Sessions.Single(entry => entry.Authority.SessionId == session);
            return ValueTask.FromResult(new SessionWorkSnapshot(target, 1, Time.Now, new(session, target.Authority.Generation, 0, []),
                0, limits.PendingPerSession, limits.ExecutionSlots, [], 0,
                [.. Tasks.Values.Where(task => task.Task.Request.SessionId == session).OrderBy(task => task.Task.Request.TaskId.Value)],
                0, [], 0));
        }
    }
}