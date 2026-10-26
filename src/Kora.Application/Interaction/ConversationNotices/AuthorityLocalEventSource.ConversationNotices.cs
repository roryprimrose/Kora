using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

public sealed partial class AuthorityLocalEventSource
{
    private readonly Lock conversationEpochGate = new();
    private long conversationAdmission = access.ControlRevision;
    private DateTimeOffset conversationNotBefore = DateTimeOffset.MinValue;

    private IReadOnlyList<LocalEvent> ReadConversationNotices(SessionWorkSnapshot snapshot, long revision)
    {
        var now = time.GetUtcNow();
        DateTimeOffset notBefore;
        lock (conversationEpochGate)
        {
            if (conversationAdmission != revision)
            {
                conversationAdmission = revision;
                if (now > conversationNotBefore) { conversationNotBefore = now; }
            }
            notBefore = conversationNotBefore;
        }
        return [.. FromWork(snapshot, now), .. ConversationEvents(snapshot, now, notBefore)];
    }

    internal static IReadOnlyList<LocalEvent> ConversationEvents(
        SessionWorkSnapshot snapshot, DateTimeOffset now, DateTimeOffset notBefore)
    {
        if (!snapshot.Session.Authority.IsActive) { return []; }
        var result = new List<LocalEvent>();
        foreach (var task in snapshot.Tasks)
        {
            if (task.ConversationNotice is not { } notice) { continue; }
            notice.Validate(task.Task);
            if (!string.Equals(task.Source, ConversationNoticeAuthority.Source, StringComparison.Ordinal)
                || task.Generation != snapshot.Session.Authority.Generation || notice.Generation != task.Generation
                || notice.Request.SessionId != snapshot.Session.Authority.SessionId
                || notice.SubmittedAt <= notBefore || notice.SubmittedAt > now)
            { continue; }
            var expiry = notice.QuestionExpiresAt ?? notice.SubmittedAt + ConversationNoticeAuthority.Lifetime;
            if (expiry <= now) { continue; }
            if (notice.Question is { } key && !snapshot.PendingQuestions.Any(question =>
                question.Key == key && question.Generation == notice.Generation
                && question.Status == QuestionStatus.Pending && question.AnswerChannel is null
                && question.Kind is QuestionKind.Text or QuestionKind.SingleChoice && question.ExpiresAt == expiry))
            { continue; }
            var source = notice.Question is null ? LocalEventSource.ConversationTask : LocalEventSource.ConversationQuestion;
            var subject = notice.Question?.QuestionId.Value ?? notice.Request.TaskId.Value;
            var type = notice.Question is not null ? LocalEventType.UserAttention
                : task.Task.State == Kora.Core.Hosting.HostTaskState.Succeeded ? LocalEventType.Completed
                : task.Task.State == Kora.Core.Hosting.HostTaskState.Unknown ? LocalEventType.Unknown : LocalEventType.Failed;
            var item = new LocalEvent(LocalEvent.Identity(source, notice.Request.SessionId.Value, subject),
                1, source, type, notice.Request.SessionId, notice.Request.TaskId, subject,
                notice.Generation.Value, notice.Question?.Revision.Value ?? notice.MessageRevision,
                notice.MessageRevision, notice.Request.Origin, null, now, expiry);
            item.Validate();
            result.Add(item);
        }
        return result;
    }
}
