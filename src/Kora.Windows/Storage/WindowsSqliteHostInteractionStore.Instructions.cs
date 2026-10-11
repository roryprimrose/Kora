using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IInstructionControlSessionStore
{
    public ValueTask<WorkSessionAuthorization> CreateInstructionSession(
        HostRequest request, Func<bool> admitted, CancellationToken token) =>
        RunHostMutationAsync(request, "session.create.instruction-control", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null || !admitted())
            { throw new InvalidOperationException("Instruction admission changed or its identity already exists."); }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit, changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            return session;
        }, token, admitted, requireIdle: false);

    public ValueTask<T> WithInstructionSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token) =>
        WithCurrentControlSessionAsync(request, generation, operation, token);
}
