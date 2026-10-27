using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal McpResultFollowUpWorkflow CreateMcpResultFollowUpWorkflow(IMcpResultSourceUse? sourceUse = null) =>
        new(this, sourceUse ?? UnavailableMcpResultSourceUse.Instance, time);

    internal async Task<HostTaskRecord> ValidateMcpObservationAsync(McpResultObservation observation,
        Func<bool> admitted, CancellationToken token)
    {
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == observation.Control && admitted()
            && time.GetUtcNow() >= observation.Result.ObservedAt && time.GetUtcNow() < observation.Invocation.ExpiresAt;
        token.ThrowIfCancellationRequested();
        if (!Live()) { throw new InvalidOperationException("The original private MCP result is no longer admitted."); }
        var target = await store.ReadMetadataAsync(observation.Request.SessionId, token).ConfigureAwait(false);
        var task = await store.ReadTaskAsync(observation.Request.SessionId, observation.Request.TaskId, token).ConfigureAwait(false);
        if (!Live() || target != observation.Target || !target.Authority.IsActive
            || task is not { CurrentSource: true, Task.State: HostTaskState.Succeeded }
            || task.Generation != target.Authority.Generation || task.Task.Request != observation.Request)
        { throw new InvalidOperationException("The exact completed MCP task or Active conversation was deleted, changed or retired."); }
        return task.Task;
    }

    internal async Task<ConversationSubmissionReceipt> SubmitMcpResultFollowUpAsync(McpResultFollowUpReview review,
        Func<bool> admitted, Func<CancellationToken, Task> validate, CancellationToken token)
    {
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), review.Observation.Request.SessionId,
            new(Guid.NewGuid()), review.Origin), HostActivityLayer.Application, HostOperation.Request, [review.Observation.Cause]);
        try
        {
            var receipt = await SubmitReviewedContextAsync(review.Observation.Target, review.Question, review.CreateEnvelope,
                admitted, validate, [review.Observation.Cause], review.Origin, token).ConfigureAwait(false);
            activity.Complete(receipt.Message.Outcome switch
            {
                ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
                ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
                ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
                _ => HostOperationOutcome.Failed,
            });
            return receipt;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    internal Task JoinMcpResultFollowUpAsync() =>
        execution?.WaitForQuiescenceAsync(CancellationToken.None) ?? Task.CompletedTask;
}
