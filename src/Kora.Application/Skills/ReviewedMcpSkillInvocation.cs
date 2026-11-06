using Kora.Core.Mcp;

namespace Kora.Application.Skills;

/// <summary>Single-use original-user selection; the declaration cannot choose the account or grant execution.</summary>
public sealed class ReviewedMcpSkillInvocation : IDisposable
{
    private int consumed;
    internal ReviewedMcpSkillInvocation(ReviewedToolSkillReview review, string body, string serverId,
        McpAccountReference account, string arguments)
    { Review = review; Body = body; ServerId = serverId; Account = account; Arguments = arguments; }

    internal ReviewedToolSkillReview Review { get; }
    public string Body { get; private set; }
    public string ServerId { get; }
    public McpAccountReference Account { get; }
    public string Arguments { get; private set; }

    internal void Consume()
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0)
        { throw new InvalidOperationException("This MCP skill invocation was already attempted or closed. No replay is admitted."); }
    }

    public void Dispose() { Interlocked.Exchange(ref consumed, 1); Body = string.Empty; Arguments = string.Empty; }
}
