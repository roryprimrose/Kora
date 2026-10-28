using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    internal delegate Task<ConversationHandoffCommit?> LocalUnavailableContinuation(
        ModelTurn turn, ModelContextEnvelope context, Func<bool> admitted, CancellationToken token);

    internal Task<ConversationSubmissionReceipt> SubmitLocalFirstAsync(SessionWorkspaceEntry target, string body,
        Func<bool> admitted, LocalUnavailableContinuation handoff, CancellationToken token) =>
        SubmitCoreAsync(target, body, admitted, null, null, RequestOrigin.LocalUi,
            (request, now) => new(request, Policy, body, [], now, now.AddSeconds(15), ModelToolMode.None),
            ModelTurnChoice.Local, token, localHandoff: handoff);

    internal async Task<ConversationSubmissionReceipt> ExecuteHandoffSuccessorAsync(SessionWorkspaceEntry target,
        ConversationSubmissionReceipt retained, ModelTurnAdmission admission, ModelContextEnvelope context,
        Func<bool> admitted, CancellationToken token)
    {
        if (admission.Turn is not null)
        {
            return await ExecuteRetainedAsync(target, retained, admission, context, admitted, null, token).ConfigureAwait(false);
        }
        var result = admission.Result;
        if (result.Outcome == ModelTurnOutcome.Succeeded || result.Response is not null)
        { throw new InvalidDataException("A handoff without an admitted turn cannot claim success."); }
        var terminal = await conversations.CompleteConversationSubmissionAsync(context.Request, result.Outcome,
            result.Reason, admitted, token).ConfigureAwait(false);
        await RequireSubmissionReadbackAsync(terminal, target, context.Request, context.UserRequest, admitted, null, token).ConfigureAwait(false);
        return terminal;
    }
}
