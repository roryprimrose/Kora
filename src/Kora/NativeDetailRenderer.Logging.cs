using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NativeDetailRenderer
{
    [LoggerMessage(310, LogLevel.Warning,
        "Native detail source fallback: {Reason}, ItemId {ItemId}, Revision {Revision}, Profile {Profile}, Blocks {Blocks}, Nodes {Nodes}, Depth {Depth}.")]
    private static partial void RenderFallback(
        ILogger logger, string reason, Guid? itemId, long? revision, string profile, int blocks, int nodes, int depth);
}
