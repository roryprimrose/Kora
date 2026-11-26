using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<ConversationSubmissionReceipt> SubmitReviewedClaudeCustomizationAsync(SessionWorkspaceEntry target,
        string body, SelectedHostedDefinition selection, Func<bool> admitted,
        Func<CancellationToken, Task> validateSource, IEnumerable<ActivityLink> links,
        RequestOrigin origin, CancellationToken token) =>
        SubmitCoreAsync(target, body, admitted, validateSource, links, origin,
            (request, now) => new(request, Policy, body, [], now, now.Add(HostedTurnAuthorization.MaximumDuration), ModelToolMode.None),
            ModelTurnChoice.Hosted, token, claude: true, joinCancelledResources: true, ordinaryActiveDeadline: true,
            claudeCustomization: new(selection, async cancellation =>
            {
                await validateSource(cancellation).ConfigureAwait(false);
                return admitted();
            }));

    private sealed record ClaudeCustomizationRequest(SelectedHostedDefinition Selection,
        Func<CancellationToken, ValueTask<bool>> SourceCurrent);
}
