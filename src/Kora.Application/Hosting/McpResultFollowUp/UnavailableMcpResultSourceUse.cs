using Kora.Core.Hosting;

namespace Kora.Application.Hosting.McpResultFollowUp;

internal sealed class UnavailableMcpResultSourceUse : IMcpResultSourceUse
{
    internal static UnavailableMcpResultSourceUse Instance { get; } = new();
    public McpResultSourceUseDecision Check(McpResultObservation observation, string question, RequestOrigin questionOrigin,
        CancellationToken token) =>
        new(McpResultSourceUseKind.Unavailable, 0);
}
