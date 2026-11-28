using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private OrdinaryTurnCheckIns StartCheckIns(ModelTurnHost runtime, ModelTurn turn, ConversationCancellationLease running)
    {
        runtime.UseOrdinaryCheckIns(turn);
        var checkIns = new OrdinaryTurnCheckIns(turn.Lifetime,
            ordinaryDeadlines?.Capture() ?? Kora.Core.Configuration.OrdinaryTurnDeadline.Default, time, cancellationAudit,
            running.Target.LeaseId, running.PresentCheckIn, () =>
            {
                // Without the trusted audit log a check-in cannot be posted, so the turn stops rather than run silently.
                try { _ = running.RequestCancellationAsync(static () => { }); }
                catch (InvalidOperationException exception) { Failure(logger, exception.GetType().Name); }
            });
        running.CheckIns = checkIns;
        return checkIns;
    }

    /// <summary>The pending check-ins of every running ordinary turn, for exact single-target voice replies.</summary>
    public IReadOnlyList<ConversationCheckInTarget> CapturePendingCheckIns()
    {
        lock (cancellationGate)
        {
            return runningConversations.Values
                .Where(running => running.Status is { Phase: ConversationExecutionPhase.Running, CheckIn: not null })
                .Select(running => new ConversationCheckInTarget(running.Target, running.Status.CheckIn!))
                .ToArray();
        }
    }

    public bool IsCheckInCurrent(ConversationCheckInTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (cancellationGate)
        {
            return runningConversations.TryGetValue(target.Execution.Request.SessionId, out var running)
                && running.Target == target.Execution && running.CanContinue
                && running.Status is { Phase: ConversationExecutionPhase.Running } status && status.CheckIn == target.CheckIn;
        }
    }

    /// <summary>
    /// Answers the exact pending check-in of an observed running ordinary turn. Keep-going choices reschedule
    /// or remove later check-ins; Stop uses the ordinary audited running-conversation cancellation path.
    /// </summary>
    public async Task<ConversationExecutionStatus> AnswerCheckInAsync(ConversationCheckInTarget target,
        ConversationCheckInChoice choice, Func<bool> admitted, CancellationToken token, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(choice);
        RequireOriginalSource(origin);
        var revision = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == revision && admitted() && IsCheckInCurrent(target);
        ConversationCancellationLease running;
        lock (cancellationGate)
        {
            if (!runningConversations.TryGetValue(target.Execution.Request.SessionId, out var found)
                || found.Target != target.Execution || found.CheckIns is null)
            { throw new InvalidOperationException("Only the exact observed running turn can answer its check-in."); }
            running = found;
        }
        var initiator = origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser;
        var control = new HostRequest(new(Guid.NewGuid()), target.Execution.Request.SessionId, target.Execution.Request.TaskId, origin);
        if (choice.Operation == ConversationCheckInOperation.Stop)
        {
            if (!Live()) { throw new InvalidOperationException("The check-in or private user control changed; nothing was stopped."); }
            Action<bool> completeStop;
            using (var stopActivity = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request, [running.Link]))
            {
                completeStop = running.CheckIns.BeginStop(target.CheckIn.Id, initiator);
                stopActivity.Complete(HostOperationOutcome.Completed);
            }
            var accepted = false;
            try
            {
                var stopped = await CancelRunningConversationAsync(target.Execution, admitted, token, origin).ConfigureAwait(false);
                accepted = true;
                return stopped;
            }
            finally
            {
                using var receipt = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request, [running.Link]);
                completeStop(accepted);
                receipt.Complete(accepted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            }
        }
        using var activity = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request, [running.Link]);
        try
        {
            var session = await store.ReadMetadataAsync(target.Execution.Request.SessionId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Live() || !session.Authority.IsActive || session.Authority.Generation != target.Execution.Generation)
            { throw new InvalidOperationException("The check-in, session or private user control changed; nothing was answered."); }
            running.CheckIns.Answer(target.CheckIn.Id, choice, initiator);
            activity.Complete(HostOperationOutcome.Completed);
            return running.Status;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            throw;
        }
    }
}
