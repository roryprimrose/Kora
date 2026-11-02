using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal Task<LocalRequestProfileReview> ReviewLocalRequestProfileAsync(SessionWorkspaceEntry target, string body,
        RequestOrigin origin, ProviderProfileTuple profile, Func<bool> admitted, CancellationToken token) =>
        (turns as ModelTurnHost ?? throw new InvalidOperationException("The request-only profile host is unavailable."))
            .ReviewLocalRequestProfileAsync(target, body, origin, profile, admitted, token);

    internal Task<ConversationSubmissionReceipt> SubmitLocalRequestProfileAsync(LocalRequestProfileReview review,
        Func<bool> admitted, Func<CancellationToken, Task> validate, IEnumerable<ActivityLink> links, CancellationToken token) =>
        review.Profile.Service == ProviderService.Local
            ? SubmitOrdinaryWithScopeAsync(review.Target, review.Body, () => admitted() && review.IsCurrent,
                validate, links, review.Origin, true, null, token, review)
            : SubmitCoreAsync(review.Target, review.Body, admitted, validate, links, review.Origin,
                (request, now) => new(request, Policy, review.Body, [], now,
                    now.Add(HostedTurnAuthorization.MaximumDuration), Kora.Core.Dependencies.ModelToolMode.None),
                Kora.Core.Dependencies.ModelTurnChoice.Hosted, token,
                copilot: review.Profile.Service == ProviderService.Copilot,
                claude: review.Profile.Service == ProviderService.Claude,
                joinCancelledResources: true, localRequestProfile: review);
}
