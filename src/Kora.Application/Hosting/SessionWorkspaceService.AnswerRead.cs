using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task<ConversationResponse> ReadRetainedAssistantAnswerAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            var control = access.ControlRevision;
            expected.Validate();
            void RequireCurrent()
            {
                token.ThrowIfCancellationRequested();
                if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    || request.InvocationId is not null || HostActivity.Current?.Request != request
                    || request.SessionId != target.Authority.SessionId
                    || expected.Request.SessionId != request.SessionId || expected.AssistantResponse is null
                    || !access.CanControl || access.ControlRevision != control || !admitted())
                { throw new InvalidOperationException("One exact retained ASSISTANT answer requires unchanged original-user channel and private session authority."); }
            }
            RequireCurrent();
            var actualTarget = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            RequireCurrent();
            var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            RequireCurrent();
            if (actualTarget != target || actual != expected)
            { throw new InvalidOperationException("The exact retained answer, source generation or session changed; nothing was substituted."); }
            var finalTarget = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            RequireCurrent();
            var final = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            RequireCurrent();
            if (finalTarget != target || final != expected)
            { throw new InvalidOperationException("Retained answer source changed before disclosure. Make a fresh deliberate selection."); }
            return actual.AssistantResponse!;
        }, token);
}
