using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IAuditRetentionSessionStore
{
    public ValueTask<WorkSessionAuthorization> CreateAuditRetentionSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, "session.create.audit-retention-control", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null || !admitted())
            {
                throw new InvalidOperationException("Audit retention admission changed or identity already exists.");
            }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            return session;
        }, cancellationToken, admitted, requireIdle: false);

    public ValueTask<T> WithAuditRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken) =>
        WithCurrentControlSessionAsync(request, generation, operation, cancellationToken);
}
