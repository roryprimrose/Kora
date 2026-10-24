using Kora.Application.Agents;
using Kora.Core.Hosting;

namespace Kora.Application.Skills;

public sealed partial class SharedSkillInvocationWorkflow
{
    internal Kora.Core.Skills.SharedSkillSnapshot RequireAgentSnapshot(SharedSkillUseReview review) => review.Require(this);

    public async Task<AgentSkillGuidanceLease> AcquireForAgentAsync(SharedSkillUseReview review,
        Func<bool> eligible, RequestOrigin origin, CancellationToken token)
    {
        try
        {
            await ValidateAsync(review, eligible, origin, token).ConfigureAwait(false);
            return await admission.RunAsync(origin, () => eligible() && review.IsCurrent,
                async (request, session, live, currentToken) =>
                    await admission.CommitAsync(request, session, live, () =>
                    {
                        var snapshot = review.Require(this);
                        if (snapshot.ReferenceBundle is null)
                        { throw new InvalidOperationException("Agent guidance requires the complete separately consented Markdown bundle."); }
                        review.Consume(this);
                        return new AgentSkillGuidanceLease(snapshot, review,
                            cancellation => ValidateAsync(review, eligible, origin, cancellation));
                    }, currentToken).ConfigureAwait(false), token).ConfigureAwait(false);
        }
        catch { review.Dispose(); throw; }
    }
}
