using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IManualCallControlStore
{
    public ValueTask<WorkSessionAuthorization> CreateManualCallControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, "session.create.manual-call-control", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null || !admitted())
            {
                throw new InvalidOperationException("Manual call session admission changed or identity already exists.");
            }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [SessionChange(session, 0)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            return session;
        }, cancellationToken, admitted, requireIdle: false);

    public ValueTask<CallMutationOutcome> ApplyManualCallAsync(HostRequest request, HostRevision generation,
        bool active, Func<bool> admitted, Func<Task<CallMutationOutcome>> transition, CancellationToken cancellationToken)
    {
        RequireLive(request);
        return new(Task.Run(() => tasks.WithCommittedIntentAsync(request, async (connection, intent) =>
        {
            var session = RequireSession(connection, request.SessionId).Authority;
            if (!session.IsActive || session.Generation != generation
                || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                || intent.State != HostTaskState.IntentRecorded || intent.Revision.Value != 1 || !admitted())
            {
                throw new InvalidOperationException("Manual call requires fresh original input and a current active control session.");
            }
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                active ? "call.manual.on" : "call.manual.off", SecurityAuditOutcome.Requested,
                request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand
                    : SecurityAuditInitiator.LocalUser, Id(request.TaskId));
            using var activity = HostActivity.BeginAudit(request, audit);
            void CommitAudit(SecurityAuditEvent entry, CancellationToken token, Func<bool>? eligible = null)
            {
                using var transaction = connection.BeginTransaction();
                AppendAudit(connection, transaction, intent, session, entry);
                Commit(transaction, request, token, eligible);
            }
            CommitAudit(audit, cancellationToken, admitted);
            // The required requested audit is durable before any resource or process-memory change.
            // Both audits reuse the owning lease/connection; neither reopens interaction authority.
            void AppendTerminal(SecurityAuditOutcome terminalOutcome, string reason)
            {
                var terminal = new SecurityAuditEvent(Guid.NewGuid(), audit.Category, audit.ActionId,
                    terminalOutcome, audit.Initiator, audit.TargetId, reasonCode: reason);
                using var terminalActivity = HostActivity.BeginAudit(request, terminal);
                CommitAudit(terminal, CancellationToken.None);
                terminalActivity.Complete(terminalOutcome == SecurityAuditOutcome.Succeeded
                    ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
            CallMutationOutcome outcome;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                outcome = await transition().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AppendTerminal(SecurityAuditOutcome.Cancelled, "transition-cancelled");
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                AppendTerminal(SecurityAuditOutcome.Failed, "transition-unconfirmed");
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
            AppendTerminal(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant());
            activity.Complete(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return outcome;
        }, cancellationToken), cancellationToken));
    }
}
