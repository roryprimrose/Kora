using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private static TimeSpan RuntimeDeadline(ModelTurn turn)
    {
        var provider = turn.Registration.Adapter is CopilotModelTurnAdapter or ClaudeModelTurnAdapter
            ? HostedTurnAuthorization.MaximumDuration : Deadline;
        var constraint = turn.Context.Constraints is { } limits
            ? TimeSpan.FromMilliseconds(limits.DeadlineMilliseconds) : provider;
        return constraint < provider ? constraint : provider;
    }

    internal void CaptureOrdinaryActiveBudget(ModelTurn turn, OrdinaryTurnDeadline preference)
    {
        if (turn.Owner != owner || turn.ConversationTarget is null || turn.OrdinaryActiveBudget is not null
            || turn.QuestionContinuation is not null || turn.Context.ToolMode is not (ModelToolMode.None or ModelToolMode.ReadOnly))
        { throw new InvalidOperationException("Only a newly dispatched ordinary turn without user waits may capture this budget."); }
        var contextRemaining = turn.Context.ExpiresAt - time.GetUtcNow();
        var maximum = RuntimeDeadline(turn);
        if (contextRemaining < maximum) { maximum = contextRemaining; }
        turn.OrdinaryActiveBudget = new(preference, maximum, time);
    }
}
