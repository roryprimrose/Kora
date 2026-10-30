using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal async Task<ExpiredOrdinaryRequestSource> ReadExpiredOrdinarySourceAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token)
    {
        bool Live() => access.CanControl && admitted();
        var content = await ReadConversationDetailAsync(target, expected, Live, token).ConfigureAwait(false);
        var task = await ReadAsync(() => store.ReadTaskAsync(target.Authority.SessionId,
            expected.Request.TaskId, token), token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The exact expired task evidence is unavailable.");
        var source = new ExpiredOrdinaryRequestSource(content.ConversationMessage!, task.Task);
        source.Validate();
        _ = await ReadConversationDetailAsync(target, expected, Live, token).ConfigureAwait(false);
        var final = await ReadAsync(() => store.ReadTaskAsync(target.Authority.SessionId,
            expected.Request.TaskId, token), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (final != task || !Live())
        { throw new InvalidOperationException("The expired source, exact task revision or private control changed. Review again."); }
        return source;
    }
}
