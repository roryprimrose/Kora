using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.Mcp;

public sealed record McpExplicitResult(HostRequest Request, McpExplicitOutcome Outcome,
    HostTaskState DurableState, McpModelResult? Result)
{
    internal static McpExplicitOutcome From(CapabilityOutcome outcome) => outcome switch
    {
        CapabilityOutcome.Succeeded => McpExplicitOutcome.Succeeded,
        CapabilityOutcome.Denied => McpExplicitOutcome.Denied,
        _ => McpExplicitOutcome.Failed,
    };
}
