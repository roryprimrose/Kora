using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Application.Mcp;
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
            && time.GetUtcNow() >= observation.Result.ObservedAt && time.GetUtcNow() < observation.ExpiresAt;
        token.ThrowIfCancellationRequested();
        if (!Live()) { throw new InvalidOperationException("The original private MCP result is no longer admitted."); }
        if (observation.ValidateSource is { } validateSource) { await validateSource(token).ConfigureAwait(false); }
        var target = await store.ReadMetadataAsync(observation.Request.SessionId, token).ConfigureAwait(false);
        var task = await store.ReadTaskAsync(observation.Request.SessionId, observation.Request.TaskId, token).ConfigureAwait(false);
        if (!Live() || target != observation.Target || !target.Authority.IsActive
            || task is not { Task.State: HostTaskState.Succeeded }
            || task.Generation != target.Authority.Generation || task.Task.Request != observation.Request)
        { throw new InvalidOperationException("The exact completed MCP task or Active conversation was deleted, changed or retired."); }
        if (observation.SourceReceipt is { } source)
        {
            var message = await conversations.ReadConversationMessageAsync(observation.Request.SessionId,
                observation.Request.RequestId, token).ConfigureAwait(false);
            if (!Live() || message != source.Message || task.Task != source.Task)
            { throw new InvalidOperationException("The original MCP skill's exact confirmed message/task pair was changed or retired."); }
        }
        else if (!task.CurrentSource)
        { throw new InvalidOperationException("The original standalone MCP observation is no longer current."); }
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

    internal async Task ValidateMcpResultConfigurationAsync(string serverId, Kora.Core.Mcp.McpAccountReference account,
        string sourceRevision, CancellationToken token)
    {
        var descriptor = await (mcpConfiguration ?? throw new McpUnavailableException())
            .ResolveHttpsAsync(serverId, account, token).ConfigureAwait(false);
        if (!string.Equals(descriptor.SourceRevision, sourceRevision, StringComparison.Ordinal))
        { throw new InvalidOperationException("The original MCP result configuration was changed or retired."); }
    }
}
