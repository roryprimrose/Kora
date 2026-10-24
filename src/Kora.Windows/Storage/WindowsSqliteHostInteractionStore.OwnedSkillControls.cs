using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IOwnedSkillControlSessionStore
{
    private readonly HashSet<HostId<SessionIdentity>> ownedSkillControlSessions = [];
    private readonly Lock ownedSkillControlGate = new();

    public async ValueTask<WorkSessionAuthorization> CreateOwnedSkillSession(
        HostRequest request, Func<bool> admitted, CancellationToken token)
    {
        var session = await RunHostMutationAsync(request, "session.create.owned-skill-control",
            (connection, transaction, intent, audit) =>
            {
                if (ReadSession(connection, request.SessionId) is not null || !admitted())
                { throw new InvalidOperationException("Skill control admission changed or identity already exists."); }
                var created = new WorkSessionAuthorization(request.SessionId, new(1), true);
                var sequence = AppendAudit(connection, transaction, intent, created, audit, changes: [SessionChange(created, 0)]);
                WriteSession(connection, transaction, created, state: 0, sequence);
                return created;
            }, token, admitted, requireIdle: false).ConfigureAwait(false);
        lock (ownedSkillControlGate) { ownedSkillControlSessions.Add(session.SessionId); }
        return session;
    }

    public ValueTask<T> WithOwnedSkillSession<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken token) =>
        WithCurrentControlSessionAsync(request, generation, () =>
        {
            lock (ownedSkillControlGate)
            {
                if (!ownedSkillControlSessions.Contains(request.SessionId))
                { throw new InvalidOperationException("This is not a live owned-skill control session."); }
                return operation();
            }
        }, token);
}
