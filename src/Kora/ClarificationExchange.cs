using System.Globalization;
using System.Text.Json;

using Kora.Core.Conversation;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora;

internal sealed class ClarificationExchange(ConversationMessage message, Func<bool> admitted)
{
    internal ConversationMessage Message { get; } = message;
    internal bool IsCurrent => admitted();
    internal SessionHistoryPage? Page { get; private set; }
    internal IReadOnlyList<string> Records { get; private set; } = [];
    internal string Status { get; private set; } = "Reading a bounded retained exchange; historical records are never live responses.";

    internal void Withdraw()
    {
        Page = null;
        Records = [];
        Status = "Exchange unavailable or source changed. Read the original turn again; no newer source was substituted.";
    }

    internal void Present(SessionHistoryPage page, SessionHistoryCursor? cursor)
    {
        ValidatePage(page, cursor);
        var exact = page.Records.Where(Matches).ToArray();
        Records = exact.Select(Describe).ToArray();
        Page = page;
        var gaps = page.Records.Count(record => record.Kind == SessionHistoryKind.Gap
            || record.Availability == SessionHistoryAvailability.Gap);
        Status = string.Create(CultureInfo.InvariantCulture,
            $"Historical read only: {exact.Length} exact request/task observations in {page.Records.Length} scanned records. ")
            + (cursor is null ? "First bounded page. " : "Continuation page; earlier pages are not held here. ")
            + (page.Next is null ? "End of this fixed history snapshot. " : "Partial exchange: More exchange records advances this same snapshot. ")
            + (exact.Length == 0 ? "No exact exchange records on this page is not proof that no clarification occurred. " : string.Empty)
            + (gaps == 0 ? string.Empty : "History gap present; missing records cannot be attributed to this turn. ")
            + (exact.Any(record => record.Baseline) ? "Baseline observations: earlier order/content is unavailable. " : string.Empty)
            + "Only retained observations are shown, not a guarantee of a complete exchange. No replay, answer, approval or renewed authority.";
    }

    internal bool Matches(SessionHistoryEvent record) =>
        record.SessionId == Message.Request.SessionId
        && record.RequestId == Message.Request.RequestId.Value && record.TaskId == Message.Request.TaskId.Value;

    internal void ValidatePage(SessionHistoryPage page, SessionHistoryCursor? cursor)
    {
        if (page.SessionId != Message.Request.SessionId || page.Generation.Value <= 0 || page.Snapshot < 0
            || page.Records.IsDefault || page.Records.Length > SessionHistoryPage.MaximumRecords
            || page.Disposed || cursor is not null && (page.Generation != cursor.Generation || page.Snapshot != cursor.Snapshot))
        { throw new InvalidDataException("Clarification history requires the exact admitted bounded session snapshot."); }
        var after = cursor?.After ?? 0;
        var bytes = 0;
        foreach (var record in page.Records)
        {
            record.Validate();
            if (record.SessionId != page.SessionId || record.Generation.Value > page.Generation.Value
                || record.Sequence <= after || record.Sequence > page.Snapshot)
            { throw new InvalidDataException("Clarification history crosses session, generation or sequence bounds."); }
            after = record.Sequence;
            bytes = checked(bytes + JsonSerializer.SerializeToUtf8Bytes(record).Length);
        }
        if (bytes > SessionHistoryPage.MaximumBytes)
        { throw new InvalidDataException("Clarification history exceeds the admitted byte bound."); }
        if (page.Next is { } next)
        {
            next.Validate(page.SessionId);
            if (page.Records.IsEmpty || next.Generation != page.Generation || next.Snapshot != page.Snapshot
                || next.After != after || after >= page.Snapshot)
            { throw new InvalidDataException("Clarification continuation must advance the same bounded snapshot."); }
        }
    }

    private static string Describe(SessionHistoryEvent record)
    {
        var heading = string.Create(CultureInfo.InvariantCulture,
            $"History {record.Sequence}: {record.Kind} / {record.Availability}")
            + (record.QuestionStatus is { } status ? " / " + status : string.Empty)
            + (record.AnswerChannel is { } channel ? "\nOriginal answer channel: " + channel : string.Empty)
            + (record.QuestionStatus == QuestionStatus.Answered && record.AnswerChannel is null
                ? "\nOriginal answer channel: not retained" : string.Empty)
            + (record.TaskState is { } task ? "\nObserved task outcome: " + task : string.Empty)
            + (record.Decision is { } decision ? "\nObserved host decision: " + decision : string.Empty);
        if (record.Availability != SessionHistoryAvailability.Available)
        { return heading + "\nContent not available in this observation; no missing text is reconstructed."; }
        var question = record.Question is null ? string.Empty : "\nQuestion: " + record.Question;
        var options = string.Concat(record.Options.Select(option => "\nOption " + option.Id + ": " + option.Label));
        var answer = record.QuestionStatus == QuestionStatus.Answered
            ? (record.Answer is null ? string.Empty : "\nAccepted answer: " + record.Answer)
                + string.Concat(record.Choices.Select(choice => "\nAccepted choice: " + choice))
                + (record.Answer is null && record.Choices.IsEmpty
                    ? "\nNo accepted text or choices retained in this observation." : string.Empty)
            : "\nNo accepted answer in this observation.";
        return heading + question + options + answer;
    }
}
