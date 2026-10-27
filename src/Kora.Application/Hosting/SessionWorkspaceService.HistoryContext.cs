using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task ValidateConversationHistoryAsync(SessionWorkspaceEntry target,
        IReadOnlyList<ConversationHistorySelection> selections, Func<bool> admitted, CancellationToken token,
        ConversationMessage? advisorySource = null)
    {
        var ordered = ConversationHistoryContext.ValidateAndOrder(target.Authority.SessionId, selections);
        if (advisorySource is not null && ordered.Any(item => item.Message.Sequence >= advisorySource.Sequence))
        { throw new InvalidDataException("Select only whole messages strictly earlier than the exact advisory turn."); }
        var sources = ordered.Select(item => item.Message).DistinctBy(message => message.Request.RequestId);
        return ValidateConversationHistorySourcesAsync(target,
            advisorySource is null ? sources : sources.Prepend(advisorySource), admitted, token);
    }

    internal Task ValidateModelHistoryContextSourceAsync(SessionWorkspaceEntry target,
        ConversationMessage source, Func<bool> admitted, CancellationToken token) =>
        ValidateConversationHistorySourcesAsync(target, [source], admitted, token);

    private Task<bool> ValidateConversationHistorySourcesAsync(SessionWorkspaceEntry target,
        IEnumerable<ConversationMessage> sources, Func<bool> admitted, CancellationToken token) =>
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
            if (!target.Authority.IsActive
                || await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("The exact Active conversation source changed. Review again explicitly."); }
            foreach (var source in sources)
            {
                RequireCurrent();
                var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                    source.Request.RequestId, token).ConfigureAwait(false);
                RequireCurrent();
                if (actual != source)
                { throw new InvalidOperationException("A selected retained message changed or was deleted. No newer source was substituted."); }
            }
            if (await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
            { throw new InvalidOperationException("History session generation or metadata changed during private inspection."); }
            RequireCurrent();
            return true;
        }, token);
}
