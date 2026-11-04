using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Exact volatile result and original task reviewed before one local model continuation.</summary>
public sealed record McpContinuationReview(McpModelResult Result, string Question, string ConversationName,
    RequestOrigin Origin, long PolicyRevision)
{
    public override string ToString() => nameof(McpContinuationReview);
}
