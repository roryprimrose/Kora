using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora.Application.Agents;

/// <summary>Consumed shared-skill authority for inert local evidence only, not a skill invocation.</summary>
public sealed class AgentSkillGuidanceLease : IDisposable
{
    private readonly SharedSkillUseReview review;
    private readonly Func<CancellationToken, Task> validate;
    private int retired;

    internal AgentSkillGuidanceLease(SharedSkillSnapshot snapshot, SharedSkillUseReview review,
        Func<CancellationToken, Task> validate)
    { Snapshot = snapshot; this.review = review; this.validate = validate; }

    public SharedSkillSnapshot Snapshot { get; }
    public bool IsCurrent => Volatile.Read(ref retired) == 0 && review.IsCurrent;

    public async Task ValidateAsync(CancellationToken token)
    {
        if (!IsCurrent) { throw new InvalidOperationException("Exact single-use agent skill guidance was retired."); }
        await validate(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!IsCurrent) { throw new InvalidOperationException("Agent guidance expired during validation."); }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref retired, 1);
        review.Dispose();
    }
}
