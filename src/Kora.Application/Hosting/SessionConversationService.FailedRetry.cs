using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal sealed record RetryIntent(FailedRequestRetrySource Source,
        ModelTurnHost.RetryProviderReview Provider, Func<CancellationToken, Task> ValidateSource);

    internal SessionRequestCommandService CreateRequestCommands() => new(workspace, this, access, time);

    internal Task<ModelTurnHost.RetryProviderReview> ReviewRetryProviderAsync(SessionRequestReview review,
        Func<bool> admitted, CancellationToken token) =>
        (turns as ModelTurnHost ?? throw new InvalidOperationException("The owning model host cannot review an original-provider retry."))
        .ReviewRetryProviderAsync(review.Target, review.FailedSource!.Provider, admitted, token);

    internal async Task<ConversationSubmissionReceipt> SubmitFailedRetryAsync(SessionRequestReview review,
        ModelTurnHost.RetryProviderReview provider, Func<bool> admitted, Func<CancellationToken, Task> validateSource,
        IEnumerable<ActivityLink> links, Action accepted, CancellationToken token)
    {
        var retry = new RetryIntent(review.FailedSource!, provider, validateSource);
        var submitted = await SubmitQueuedCoreAsync(review.Target, review.Body, admitted, validateSource,
            links, review.Origin, false, accepted, retry, token).ConfigureAwait(false);
        if (submitted.Position > 0) { return submitted.Receipt; }
        try { return (await submitted.Completion.WaitAsync(token).ConfigureAwait(false)).RequireReceipt(); }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested)
        { throw new ConversationSubmissionUnconfirmedException(submitted.Receipt.Message.Request, exception); }
    }
}
