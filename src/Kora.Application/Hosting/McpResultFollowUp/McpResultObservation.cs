using System.Diagnostics;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Mcp.ResultFollowUp;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.McpResultFollowUp;

/// <summary>Issued only at the successful host completion callback, never from rendered text or provider JSON.</summary>
internal sealed class McpResultObservation
{
    internal McpResultObservation(SessionWorkspaceEntry target, HostRequest request, McpModelResult result,
        HostOperationProposal invocation, McpReview mapping, HostRevision generation, long control,
        string serverId, string serverName, ActivityLink cause)
    {
        if (request.SessionId != target.Authority.SessionId || !target.Authority.IsActive
            || generation != target.Authority.Generation || !invocation.Request.IsWithinIntent(request)
            || invocation.Effect != HostOperationEffect.BoundedRead || mapping.Stage != McpReviewStage.Invocation
            || !mapping.IsBoundTo(invocation) || mapping.ToolName is null || mapping.InputSchema is null
            || !string.Equals(result.ToolId, "mcp." + mapping.ToolName, StringComparison.Ordinal)
            || !string.Equals(result.SourceRevision, invocation.Binding.TrackedContentDigest, StringComparison.Ordinal))
        { throw new InvalidDataException("Only a completed exact host-mapped read-only MCP observation can be captured."); }
        Target = target;
        Request = request;
        Result = result;
        Invocation = invocation;
        Mapping = mapping;
        Control = control;
        ServerId = serverId;
        Cause = cause;
        Provenance = new(serverName, mapping.Endpoint.AbsoluteUri, mapping.AccountDisplayName, mapping.ToolName, request.Origin);
    }

    internal Guid Nonce { get; } = Guid.NewGuid();
    internal SessionWorkspaceEntry Target { get; }
    internal HostRequest Request { get; }
    internal McpModelResult Result { get; }
    internal HostOperationProposal Invocation { get; }
    internal McpReview Mapping { get; }
    internal long Control { get; }
    internal string ServerId { get; }
    internal ActivityLink Cause { get; }
    internal McpResultProvenance Provenance { get; }
    public override string ToString() => nameof(McpResultObservation);
}
