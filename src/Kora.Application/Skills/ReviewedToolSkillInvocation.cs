using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Single-use confirmation issued only by the reviewed tool-capable workflow.</summary>
public sealed class ReviewedToolSkillInvocation : IDisposable
{
    private string body;
    private int consumed;

    internal ReviewedToolSkillInvocation(ReviewedToolSkillReview review, string body, Uri destination)
    { Review = review; this.body = body; Destination = destination; }

    internal ReviewedToolSkillReview Review { get; }
    public SessionWorkspaceEntry Target => Review.Target;
    public RequestOrigin Origin => Review.Origin;
    public string Body => body;
    public Uri Destination { get; }

    internal string Consume()
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("Invocation already attempted or closed; inspect the original retained receipt without replay."); }
        return body;
    }

    public void Dispose() { Interlocked.Exchange(ref consumed, 1); body = string.Empty; }
}
