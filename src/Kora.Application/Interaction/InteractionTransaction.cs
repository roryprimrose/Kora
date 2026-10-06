using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

internal sealed class InteractionTransaction(IHostInteractionStore store)
{
    public async ValueTask<HostInteractionDecision> RunAsync(HostRequest request, string action,
        Func<HostInteractionSnapshot, (HostInteractionSnapshot Snapshot, HostInteractionDecision Decision)> transition,
        CancellationToken cancellationToken)
    {
        if (HostActivity.RequireCurrent().Request != request)
        {
            throw new InvalidOperationException("The interaction requires its live owning host activity.");
        }
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            action, SecurityAuditOutcome.Requested, request.Origin switch
            {
                RequestOrigin.LocalUi => SecurityAuditInitiator.TypedCommand,
                RequestOrigin.ActivatedVoice => SecurityAuditInitiator.VoiceCommand,
                _ => SecurityAuditInitiator.System,
            }, request.TaskId.Value.ToString("D"));
        using var activity = HostActivity.BeginAudit(request, audit);
        try
        {
            var result = await store.TransactAsync(request, snapshot => PrepareCommit(request, audit, snapshot, transition),
                cancellationToken).ConfigureAwait(false);
            activity.Complete(result.Outcome is HostInteractionOutcome.Denied or HostInteractionOutcome.Conflict or HostInteractionOutcome.Expired
                ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private static HostInteractionCommit PrepareCommit(HostRequest request, SecurityAuditEvent audit,
        HostInteractionSnapshot snapshot,
        Func<HostInteractionSnapshot, (HostInteractionSnapshot Snapshot, HostInteractionDecision Decision)> transition)
    {
        if (!OwnsIntent(snapshot.Intent.Request, request) || snapshot.Intent.IsTerminal
            || snapshot.Session.SessionId != request.SessionId || snapshot.Session.Generation.Value <= 0)
        {
            throw new InvalidDataException("Interaction storage has no matching committed host intent/session.");
        }
        foreach (var grant in snapshot.Grants)
        {
            grant.Validate();
        }
        var (next, decision) = transition(snapshot);
        var outcome = decision.Outcome switch
        {
            HostInteractionOutcome.Denied or HostInteractionOutcome.Conflict or HostInteractionOutcome.Expired => SecurityAuditOutcome.Denied,
            HostInteractionOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
            _ => SecurityAuditOutcome.Succeeded,
        };
        return new(next, decision, new SecurityAuditEvent(audit.CorrelationId, audit.Category,
            audit.ActionId, outcome, audit.Initiator, audit.TargetId,
            decision.Grant?.Id.Value ?? decision.Question?.Key.QuestionId.Value, decision.Reason));
    }

    private static bool OwnsIntent(HostRequest intent, HostRequest request) =>
        intent.RequestId == request.RequestId && intent.SessionId == request.SessionId
        && intent.TaskId == request.TaskId && intent.Origin == request.Origin
        && (intent.InvocationId is not { } invocation
            || (request.InvocationId is { } requested && invocation == requested));

    public static (HostInteractionSnapshot, HostInteractionDecision) Reject(
        HostInteractionSnapshot snapshot, string reason, HostInteractionOutcome outcome = HostInteractionOutcome.Denied) =>
        (snapshot, new(outcome, reason));

    public static bool CanInteract(HostInteractionSnapshot snapshot) =>
        snapshot.Session.IsActive && snapshot.Policy.IsUnlocked;

    public static int FindQuestion(HostInteractionSnapshot snapshot, HostQuestionKey key) =>
        snapshot.Questions.FindIndex(question => question.Key == key);
}
