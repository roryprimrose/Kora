using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task<AdmittedDetailContent> ReadConversationNoticeResultAsync(SessionWorkspaceEntry target,
        LocalEvent notice, Func<bool> admitted, CancellationToken token) =>
        ReadAsync(async () =>
        {
            notice.Validate();
            if (notice.Source != LocalEventSource.ConversationTask || !target.Authority.IsActive
                || notice.SessionId != target.Authority.SessionId || notice.Generation != target.Authority.Generation.Value)
            { throw new InvalidOperationException("Unavailable: select a current terminal conversation notice from its exact Active session. Inspect ordinary history separately."); }

            var before = await ReadWorkAsync(notice.SessionId, token).ConfigureAwait(false);
            var source = RequireNoticeResultSource(before, target, notice);
            var message = await Conversations.ReadConversationMessageAsync(notice.SessionId,
                source.Request.RequestId, token).ConfigureAwait(false);
            message.Validate();
            if (!admitted() || message.Request != source.Request || message.Generation != source.Generation
                || message.Revision != source.MessageRevision || message.SubmittedAt != source.SubmittedAt
                || message.Execution?.Turn != source.Turn || message.Outcome != source.Outcome || message.Reason != source.Reason)
            { throw new InvalidOperationException("Unavailable: the exact retained terminal result changed. Inspect ordinary history separately; no newer answer was substituted."); }
            if (message.AssistantResponse is null)
            { throw new InvalidOperationException("Unavailable: this exact terminal has no retained ASSISTANT answer. Inspect its typed HOST outcome in Work or ordinary history; no answer was fabricated."); }

            var content = await ReadConversationDetailAsync(target, message, admitted, token, assistant: true).ConfigureAwait(false);
            var after = await ReadWorkAsync(notice.SessionId, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!admitted() || RequireNoticeResultSource(after, target, notice) != source)
            { throw new InvalidOperationException("Unavailable: terminal notice authority changed before disclosure. Inspect ordinary history separately."); }
            return content;
        }, token);

    private static ConversationNoticeAuthority RequireNoticeResultSource(SessionWorkSnapshot snapshot,
        SessionWorkspaceEntry target, LocalEvent notice)
    {
        snapshot.RequireSubject(notice.SessionId);
        var task = snapshot.Tasks.SingleOrDefault(item => item.Task.Request.TaskId == notice.TaskId);
        if (snapshot.Session != target || task is not { ConversationNotice: { Question: null, Outcome: not null } source }
            || !string.Equals(task.Source, ConversationNoticeAuthority.Source, StringComparison.Ordinal))
        { throw new InvalidOperationException("Unavailable: the exact current-run terminal source is missing or retired. Inspect ordinary history separately."); }
        source.Validate(task.Task);
        var type = task.Task.State == HostTaskState.Succeeded ? LocalEventType.Completed
            : task.Task.State == HostTaskState.Unknown ? LocalEventType.Unknown : LocalEventType.Failed;
        if (source.Request.SessionId != notice.SessionId
            || source.Request.TaskId.Value != notice.SubjectId || source.Generation.Value != notice.Generation
            || task.Generation != source.Generation || source.MessageRevision != notice.SourceRevision
            || source.MessageRevision != notice.RelatedRevision || source.Request.Origin != notice.Origin
            || source.SubmittedAt + ConversationNoticeAuthority.Lifetime != notice.ExpiresAt || type != notice.Type)
        { throw new InvalidOperationException("Unavailable: terminal notice lineage does not match the retained source. Inspect ordinary history separately."); }
        return source;
    }
}
