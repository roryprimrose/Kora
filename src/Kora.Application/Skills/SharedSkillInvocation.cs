using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Immutable confirmation of original prose and one existing Active target.</summary>
public sealed class SharedSkillInvocation : IDisposable
{
    private string body;
    private int consumed;
    internal SharedSkillInvocation(SharedSkillUseReview review, SessionWorkspaceEntry target, string body, RequestOrigin origin)
    { Review = review; Target = target; this.body = body; Origin = origin; }
    internal SharedSkillUseReview Review { get; }
    public SessionWorkspaceEntry Target { get; }
    public RequestOrigin Origin { get; }
    public string Body => body;
    internal string Consume()
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("Invocation already attempted or closed; inspect the original receipt without replay."); }
        return body;
    }
    public void Dispose() { Interlocked.Exchange(ref consumed, 1); body = string.Empty; }
}
