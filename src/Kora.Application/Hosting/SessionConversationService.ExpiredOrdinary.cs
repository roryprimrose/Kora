using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal sealed record FreshOrdinaryIntent(ExpiredOrdinaryRequestSource Source,
        ModelTurnHost.RetryProviderReview Provider, DateTimeOffset ConfirmedAt);

    internal Func<bool> ReviewBareOrdinaryRequest(SessionWorkspaceEntry target) =>
        sessionInstructionScopes?.ReviewBareRequest(target) ?? (static () => true);

    internal Task<ModelTurnHost.RetryProviderReview> ReviewCurrentLocalProviderAsync(SessionWorkspaceEntry target,
        Func<bool> admitted, CancellationToken token) =>
        (turns as ModelTurnHost ?? throw new InvalidOperationException("The current local provider review is unavailable."))
            .ReviewRetryProviderAsync(target, null, admitted, token);

    internal async Task<ConversationSubmissionReceipt> SubmitFreshOrdinaryAsync(SessionRequestReview review,
        ModelTurnHost.RetryProviderReview provider, DateTimeOffset confirmedAt,
        Func<bool> admitted, Func<CancellationToken, Task> validate, IEnumerable<ActivityLink> links,
        Action accepted, CancellationToken token)
    {
        var submitted = await SubmitQueuedCoreAsync(review.Target, review.Body, admitted, validate, links, review.Origin,
            true, accepted, null, token, new(review.ExpiredSource!, provider, confirmedAt)).ConfigureAwait(false);
        if (submitted.Position > 0) { return submitted.Receipt; }
        try { return (await submitted.Completion.WaitAsync(token).ConfigureAwait(false)).RequireReceipt(); }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested)
        { throw new ConversationSubmissionUnconfirmedException(submitted.Receipt.Message.Request, exception); }
    }
}
