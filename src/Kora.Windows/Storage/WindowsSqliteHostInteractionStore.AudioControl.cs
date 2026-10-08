using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IAudioControlSessionStore
{
    public ValueTask<WorkSessionAuthorization> CreateAudioControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, "session.create.audio-control", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null || !admitted())
            {
                throw new InvalidOperationException("Audio control session admission changed or identity already exists.");
            }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            return session;
        }, cancellationToken, admitted, requireIdle: false);

    public ValueTask<T> WithAudioControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken)
        => WithCurrentControlSessionAsync(request, generation, operation, cancellationToken);

    private ValueTask<T> WithCurrentControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken)
    {
        RequireLive(request);
        return new(Task.Run(() => tasks.WithCommittedIntent(request, (connection, _) =>
        {
            using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            var session = RequireSession(connection, request.SessionId).Authority;
            if (!session.IsActive || session.Generation != generation)
            {
                throw new InvalidOperationException("Control session ended or its generation changed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var result = operation();
            database.VerifyFiles();
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }, cancellationToken), cancellationToken));
    }
}
