using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task<FailedRequestRetrySource> ReadFailedRetrySourceAsync(SessionWorkspaceEntry target,
        ConversationMessage expected, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            var request = HostActivity.RequireCurrent().Request;
            SessionConversationService.RequireOriginalSource(request.Origin);
            bool Live() => access.CanControl && admitted();
            if (request.SessionId != target.Authority.SessionId || request.InvocationId is not null
                || !target.Authority.IsActive || expected.Request.SessionId != target.Authority.SessionId
                || expected.Generation != target.Authority.Generation || !Live())
            { throw new InvalidOperationException("Failed-request review requires exact current original-session private control."); }
            var before = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var actual = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            var task = await store.ReadTaskAsync(target.Authority.SessionId, expected.Request.TaskId, token).ConfigureAwait(false);
            if (task is null || task.Generation != target.Authority.Generation)
            { throw new InvalidOperationException("The original failed task source is unavailable. Nothing was inferred or submitted."); }
            var source = new FailedRequestRetrySource(actual, task.Task);
            source.Validate();
            var after = await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
            var final = await Conversations.ReadConversationMessageAsync(target.Authority.SessionId,
                expected.Request.RequestId, token).ConfigureAwait(false);
            var finalTask = await store.ReadTaskAsync(target.Authority.SessionId, expected.Request.TaskId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (before != target || after != target || actual != expected || final != actual || finalTask != task || !Live())
            { throw new InvalidOperationException("The exact failed message, task revision or session changed. Review again; no newer source was substituted."); }
            return source;
        }, token);
}
