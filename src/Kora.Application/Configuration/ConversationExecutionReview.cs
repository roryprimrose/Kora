using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class ConversationExecutionReview
{
    internal ConversationExecutionReview(ConversationExecutionLimit proposed, ConversationExecutionBudget budget,
        long callRevision, WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible)
    {
        Proposed = proposed;
        Budget = budget;
        CallRevision = callRevision;
        Session = session;
        Origin = origin;
        Eligible = eligible;
    }

    public ConversationExecutionLimit Proposed { get; }
    internal ConversationExecutionBudget Budget { get; }
    internal long CallRevision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
    public string Description => $"Review shared device-local ordinary-conversation maximum: {Budget.Limit.Value} -> {Proposed.Value}. "
        + "Future resource admissions only; no dispatch, eviction or cancellation. Qualified provider/resource ceilings always win. "
        + "This does not qualify capacity two. Confirm separately or cancel.";
}
