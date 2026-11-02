using System.Diagnostics;
using Kora.Application.Hosting.McpResultFollowUp;
using Kora.Application.Mcp;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Core.Storage;

namespace Kora;

internal sealed class McpResultCapture
{
    private HostOperationProposal? proposal;
    private McpReview? mapping;
    private HostRevision generation;
    private ActivityLink cause;

    internal async Task<McpApprovalPresentation> PresentAsync(McpApprovalPresenter presenter,
        HostOperationProposal invocation, HostRevision version, McpReview review,
        Func<bool> admitted, CancellationToken token)
    {
        var link = new ActivityLink(HostActivity.RequireCurrent().Activity!.Context);
        var presentation = await presenter(invocation, version, review, admitted, token);
        if (!token.IsCancellationRequested && review.Stage == McpReviewStage.Invocation
            && review.IsBoundTo(invocation) && admitted()
            && presentation.Completed.Outcome is HostInteractionOutcome.Approved or HostInteractionOutcome.Consumed)
        {
            proposal = invocation;
            mapping = review;
            generation = version;
            cause = link;
        }
        return presentation;
    }

    internal McpResultObservation? Capture(SessionWorkspaceEntry target, HostRequest request,
        McpModelResult result, long control, string serverId, string serverName, McpAccountReference account,
        Func<CancellationToken, Task>? validateSource = null, DateTimeOffset? sourceExpiresAt = null,
        ConversationSubmissionReceipt? sourceReceipt = null)
    {
        if (proposal is not { } approved || mapping is not { } reviewed || reviewed.Account != account)
        { return null; }
        return new(target, request, result, approved, reviewed, generation, control, serverId, serverName, cause,
            validateSource, sourceExpiresAt, sourceReceipt);
    }

    internal void Retire()
    {
        proposal = null;
        mapping = null;
        cause = default;
    }
}
