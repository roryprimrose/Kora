using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task ValidateConversationHistoryAsync(SessionWorkspaceEntry target,
        IReadOnlyList<ConversationHistorySelection> selections, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            SessionConversationService.RequireOriginalSource(request.Origin);
            void RequireCurrent()
            {
                token.ThrowIfCancellationRequested();
                if (!admitted() || !access.CanControl || request.SessionId != target.Authority.SessionId)
                { throw new InvalidOperationException("History review requires unchanged private original-user exact-session admission."); }
            }
            RequireCurrent();
            var ordered = ConversationHistoryContext.ValidateAndOrder(target.Authority.SessionId, selections);
            if (!target.Authority.IsActive
                || await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("The exact Active conversation source changed. Review again explicitly."); }
            foreach (var item in ordered.DistinctBy(item => item.Message.Request.RequestId))
            {
                RequireCurrent();
                var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                    item.Message.Request.RequestId, token).ConfigureAwait(false);
                RequireCurrent();
                if (actual != item.Message)
                { throw new InvalidOperationException("A selected retained message changed or was deleted. No newer source was substituted."); }
            }
            if (await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("History session generation or metadata changed during private inspection."); }
            RequireCurrent();
            return true;
        }, token);
}
