using System.Diagnostics;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
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
        string serverId, string serverName, ActivityLink cause,
        Func<CancellationToken, Task>? validateSource = null, DateTimeOffset? sourceExpiresAt = null,
        ConversationSubmissionReceipt? sourceReceipt = null)
    {
        if (request.SessionId != target.Authority.SessionId || !target.Authority.IsActive
            || generation != target.Authority.Generation || !invocation.Request.IsWithinIntent(request)
            || invocation.Effect != HostOperationEffect.BoundedRead || mapping.Stage != McpReviewStage.Invocation
            || !mapping.IsBoundTo(invocation) || mapping.ToolName is null || mapping.InputSchema is null
            || !string.Equals(result.ToolId, "mcp." + mapping.ToolName, StringComparison.Ordinal)
            || !string.Equals(result.SourceRevision, invocation.Binding.TrackedContentDigest, StringComparison.Ordinal))
        { throw new InvalidDataException("Only a completed exact host-mapped read-only MCP observation can be captured."); }
        if (sourceReceipt is { } receipt && (mapping.Skill is null || receipt.Message.Request != request
            || receipt.Message.Generation != target.Authority.Generation || receipt.Message.Outcome != ModelTurnOutcome.Succeeded
            || receipt.Task.Request != request || receipt.Task.State != HostTaskState.Succeeded
            || receipt.Task.Revision.Value != receipt.Message.Revision))
        { throw new InvalidDataException("Only the exact confirmed successful MCP skill terminal receipt can accompany this observation."); }
        Target = target;
        Request = request;
        Result = result;
        Invocation = invocation;
        Mapping = mapping;
        Control = control;
        ServerId = serverId;
        Cause = cause;
        ValidateSource = validateSource;
        SourceReceipt = sourceReceipt;
        ExpiresAt = sourceExpiresAt is { } expiry && expiry < invocation.ExpiresAt ? expiry : invocation.ExpiresAt;
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
    internal DateTimeOffset ExpiresAt { get; }
    internal Func<CancellationToken, Task>? ValidateSource { get; }
    internal ConversationSubmissionReceipt? SourceReceipt { get; }
    internal McpResultProvenance Provenance { get; }
    public override string ToString() => nameof(McpResultObservation);
}
