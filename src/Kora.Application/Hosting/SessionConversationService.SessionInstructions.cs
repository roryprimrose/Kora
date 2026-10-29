using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Application.Dependencies;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    /// <summary>Explicit local tool-free answer through the ordinary conversation and instruction owner.</summary>
    public Task<ConversationSubmissionReceipt> SubmitLocalAnswerAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, CancellationToken token) =>
        SubmitOrdinaryQueuedAndJoinAsync(target, body, admitted, null, null, RequestOrigin.LocalUi, token, localAnswer: true);

    private async Task<ConversationSubmissionReceipt> SubmitOrdinaryWithScopeAsync(
        SessionWorkspaceEntry target, string body, Func<bool> admitted,
        Func<CancellationToken, Task>? beforeRetain, IEnumerable<ActivityLink>? links, RequestOrigin origin,
        bool localAnswer, PendingConversation? pending, CancellationToken token, LocalRequestProfileReview? localRequestProfile = null)
    {
        RequireOriginalSource(origin);
        var retry = pending?.Retry;
        if (retry is not null && retry.Source.Provider.Provider != ModelProviderIdentity.Ollama)
        {
            var provider = retry.Source.Provider.Provider;
            if (provider is not (ModelProviderIdentity.Copilot or ModelProviderIdentity.Claude))
            { throw new InvalidOperationException("The recorded provider has no admitted retry route. No fallback."); }
            return await SubmitCoreAsync(target, body, admitted, beforeRetain, links, origin,
                (request, now) => new(request, Policy, body, [], now, now.Add(HostedTurnAuthorization.MaximumDuration), ModelToolMode.None),
                ModelTurnChoice.Hosted, token, copilot: provider == ModelProviderIdentity.Copilot,
                claude: provider == ModelProviderIdentity.Claude, pending: pending).ConfigureAwait(false);
        }
        var review = sessionInstructionScopes is null ? null
            : await sessionInstructionScopes.PrepareRequestAsync(target, body, origin, token).ConfigureAwait(false);
        if (review is null)
        {
            ModelContextEnvelope Create(HostRequest request, DateTimeOffset now) =>
                new(request, Policy, body, [], now, now.AddSeconds(15), ModelToolMode.None);
            return await SubmitCoreAsync(target, body, admitted, beforeRetain, links, origin,
                localAnswer ? Create : null, localAnswer ? ModelTurnChoice.Local : ModelTurnChoice.Default, token,
                pending: pending, joinCancelledResources: localRequestProfile is not null,
                localRequestProfile: localRequestProfile).ConfigureAwait(false);
        }
        List<ActivityLink> causes = links?.ToList() ?? [];
        if (HostActivity.Current?.Activity is { } parent) { causes.Add(new(parent.Context)); }
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request, causes);
        try
        {
            if (beforeRetain is not null) { await beforeRetain(token).ConfigureAwait(false); }
            var receipt = await SubmitInstructionAsync(review,
                () => admitted() && sessionInstructionScopes!.IsRequestAdmitted(review), pending, token,
                localRequestProfile, localRequestProfile is null ? null : beforeRetain).ConfigureAwait(false);
            activity.Complete(receipt.Message.Outcome switch
            {
                ModelTurnOutcome.Succeeded => HostOperationOutcome.Completed,
                ModelTurnOutcome.Cancelled => HostOperationOutcome.Cancelled,
                ModelTurnOutcome.Unknown => HostOperationOutcome.Unknown,
                _ => HostOperationOutcome.Failed,
            });
            return receipt;
        }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { review.Retire(); }
    }
}
