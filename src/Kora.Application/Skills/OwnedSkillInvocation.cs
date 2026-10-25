using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>One immutable original request, exact Active target and complete context preview.</summary>
public sealed class OwnedSkillInvocation : IDisposable
{
    private string body;
    private string preview;
    private int consumed;
    internal OwnedSkillInvocation(OwnedSkillUseReview review, SessionWorkspaceEntry target, string body,
        string preview, OwnedSkillAuthoringAuthority authority, Func<bool> current)
    { Review = review; Target = target; this.body = body; this.preview = preview; Authority = authority; Current = current; }
    internal OwnedSkillUseReview Review { get; }
    internal OwnedSkillAuthoringAuthority Authority { get; }
    internal Func<bool> Current { get; }
    public SessionWorkspaceEntry Target { get; }
    public RequestOrigin Origin => Authority.Origin;
    public string Body => body;
    public string Preview => preview;
    internal string Consume()
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("This invocation was attempted or closed; inspect its receipt without replay."); }
        return body;
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref consumed, 1);
        body = string.Empty;
        preview = string.Empty;
    }
}
