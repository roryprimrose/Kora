using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private readonly Lock cancellationGate = new();
    private readonly Dictionary<HostId<SessionIdentity>, ConversationCancellationLease> runningConversations = [];
    public event Action<ConversationExecutionStatus>? RunningConversationChanged;

    public async Task<ConversationExecutionStatus?> ReadRunningConversationAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token)
    {
        var revision = access.ControlRevision;
        bool Live() => access.CanInspect && access.ControlRevision == revision && admitted();
        if (!Live()) { throw new InvalidOperationException("Running conversation inspection requires current private session admission."); }
        var actual = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Live() || actual != target || !actual.Authority.IsActive)
        { throw new InvalidOperationException("Refresh the exact active conversation before inspecting running work."); }
        lock (cancellationGate)
        {
            return runningConversations.TryGetValue(target.Authority.SessionId, out var running)
                && running.Target.Generation == target.Authority.Generation ? running.Status : null;
        }
    }

    public async Task<ConversationExecutionStatus> CancelRunningConversationAsync(ConversationCancellationTarget target,
        Func<bool> admitted, CancellationToken token, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        RequireOriginalSource(origin);
        var revision = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == revision && admitted();
        bool Current()
        {
            var now = time.GetUtcNow();
            return Live() && now >= target.CreatedAt && now < target.ExpiresAt;
        }
        ConversationCancellationLease running;
        lock (cancellationGate)
        {
            if (!runningConversations.TryGetValue(target.Request.SessionId, out var found) || found.Target != target)
            { throw new InvalidOperationException("Only the exact observed running conversation can be cancelled."); }
            running = found;
        }
        var control = new HostRequest(new(Guid.NewGuid()), target.Request.SessionId, target.Request.TaskId, origin);
        using var activity = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request, [running.Link]);
        try
        {
            if (!Current())
            { throw new InvalidOperationException("Cancellation observation expired or private user control was revoked."); }
            var session = await store.ReadMetadataAsync(target.Request.SessionId, token).ConfigureAwait(false);
            var task = await store.ReadTaskAsync(target.Request.SessionId, target.Request.TaskId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Current() || session.Authority.SessionId != target.Request.SessionId || !session.Authority.IsActive
                || session.Authority.Generation != target.Generation || task is null || !task.CurrentSource
                || task.Generation != target.Generation || task.Task.Request != target.Request
                || task.Task.Revision != target.TaskRevision
                || task.Task.State is not (HostTaskState.DispatchRecorded or HostTaskState.IntentRecorded))
            { throw new InvalidOperationException("The exact running task/session/generation changed; no cancellation was accepted."); }
            await running.RequestCancellationAsync(() =>
            {
                if (!Current() || running.Target != target)
                { throw new InvalidOperationException("Cancellation user authority or exact waiting/running target changed before acceptance."); }
                var audit = cancellationAudit
                    ?? throw new InvalidOperationException("Trusted cancellation audit is unavailable; cancellation was not accepted.");
                var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
                    "conversation.cancel-request", SecurityAuditOutcome.Requested,
                    origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    target.LeaseId.ToString("D"));
                audit.Write(requested);
                if (!Current())
                {
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied));
                    throw new InvalidOperationException("Cancellation user authority or freshness changed during required audit.");
                }
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
                if (!Current()) { throw new InvalidOperationException("Cancellation user authority or freshness changed after required audit."); }
            }).ConfigureAwait(false);
            CancellationAccepted(logger, target.Request.TaskId.Value, target.Generation.Value);
            activity.Complete(HostOperationOutcome.Completed);
            return running.Status;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Unknown);
            throw;
        }
    }

    private ConversationCancellationLease RegisterRunningConversation(SessionWorkspaceEntry target, ModelTurn turn,
        Func<bool> live, CancellationToken token)
    {
        var identity = new ConversationCancellationTarget(Guid.NewGuid(), turn.Provenance.Request,
            target.Authority.Generation, turn.TaskRevision, turn.Context.CreatedAt, turn.Context.ExpiresAt);
        return RegisterRunningConversation(identity, live, token);
    }

    private ConversationCancellationLease RegisterRunningConversation(ConversationCancellationTarget identity,
        Func<bool> live, CancellationToken token)
    {
        lock (cancellationGate)
        {
            RequireConversationDispatchOpen(identity.Request.SessionId);
            joinedForDisposition.Remove(identity.Request.SessionId);
            if (runningConversations.ContainsKey(identity.Request.SessionId))
            {
                throw new InvalidOperationException("The exact session already owns a running conversation.");
            }
            var lease = new ConversationCancellationLease(identity, live, status => RunningConversationChanged?.Invoke(status),
                new(HostActivity.RequireCurrent().Activity!.Context), token);
            runningConversations.Add(identity.Request.SessionId, lease);
            return lease;
        }
    }

    private void ReleaseRunningConversation(ConversationCancellationLease lease, bool confirmed)
    {
        lock (cancellationGate) { runningConversations.Remove(lease.Target.Request.SessionId); }
        lease.ReleasePhysicalCompletion(confirmed);
        if (!confirmed)
        { RunningConversationChanged?.Invoke(new(lease.Target, ConversationExecutionPhase.Unconfirmed)); }
    }
}