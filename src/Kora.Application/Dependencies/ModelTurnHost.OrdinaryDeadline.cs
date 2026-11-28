using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private static TimeSpan RuntimeDeadline(ModelTurn turn)
    {
        var provider = turn.Context.ToolMode == ModelToolMode.ApplicationMaintenance ? Kora.Core.Tools.MaintenanceTools.Deadline
            : turn.Registration.Adapter is CopilotModelTurnAdapter or ClaudeModelTurnAdapter
            ? HostedTurnAuthorization.MaximumDuration : Deadline;
        var constraint = turn.Context.Constraints is { } limits
            ? TimeSpan.FromMilliseconds(limits.DeadlineMilliseconds) : provider;
        return constraint < provider ? constraint : provider;
    }

    /// <summary>
    /// Replaces the fixed context, runtime and transport limits of a newly dispatched ordinary turn with
    /// user check-ins. The turn keeps every admission, revalidation, cancellation and audit boundary.
    /// </summary>
    internal void UseOrdinaryCheckIns(ModelTurn turn)
    {
        if (turn.Owner != owner || turn.ConversationTarget is null || turn.Lifetime.UsesCheckIns
            || turn.QuestionContinuation is not null || !turn.ContextCurrent(time)
            || turn.Context.ToolMode is not (ModelToolMode.None or ModelToolMode.ReadOnly or ModelToolMode.InteractiveConversation))
        { throw new InvalidOperationException("Only a newly dispatched, still-current ordinary turn without clarification waits may use check-ins."); }
        turn.Lifetime.UseCheckIns();
    }
}
