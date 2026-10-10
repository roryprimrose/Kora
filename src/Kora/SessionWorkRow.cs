using System.Globalization;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora;

internal sealed record SessionWorkRow(SessionQueueObservation? Queue, HostTaskObservation? Task, DateTimeOffset ObservedAt)
{
    public Guid TaskId => Queue?.Entry.Request.TaskId.Value ?? Task!.Task.Request.TaskId.Value;
    public string Identity => "Task " + TaskId.ToString("D") + " | request "
        + (Queue?.Entry.Request.RequestId.Value ?? Task!.Task.Request.RequestId.Value).ToString("D");
    public string State => Queue is { } queue
        ? "Queue: " + queue.Entry.State + " | " + queue.Eligibility
        : Task is { CurrentSource: true, Task.State: HostTaskState.IntentRecorded, Question.Status: QuestionStatus.Pending }
            ? Task.Question.ExpiresAt <= ObservedAt ? "User-wait expired; cancellation/dispatch unavailable; explicit host recovery required"
                : "Waiting for user; pre-dispatch, no active-task clock"
            : "Task: " + Task!.Task.State + (Task.Task.State is HostTaskState.Unknown ? " | Unknown quarantine; no replay"
                : Task.Task.State is HostTaskState.DispatchRecorded ? " | Current dispatch receipt; runtime progress/effect outcome not inferred"
                : Task.Task.State is HostTaskState.IntentRecorded ? " | Unclassified work; dispatch blocked" : "");
    public string Revisions => "Revision " + (Queue?.Entry.Revision.Value ?? Task!.Task.Revision.Value).ToString(CultureInfo.InvariantCulture)
        + " | generation " + (Queue?.Entry.Generation.Value ?? Task!.Generation.Value).ToString(CultureInfo.InvariantCulture)
        + (Queue is { } queue ? " | stable FIFO order " + queue.Entry.Position.ToString(CultureInfo.InvariantCulture) : " | source " + Task!.Source);
    public string Deadline => Queue is { } queue
        ? "Pending expiry " + queue.Entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            + " | captured pending lifetime " + (queue.Entry.PendingLifetimeMinutes ?? SessionQueueLimits.DefaultPendingLifetimeMinutes)
                .ToString(CultureInfo.InvariantCulture) + " minutes"
            + (queue.ActiveDeadline is { } deadline ? " | original active budget "
                + (queue.Entry.ActiveBudgetMinutes ?? SessionQueueLimits.DefaultActiveBudgetMinutes).ToString(CultureInfo.InvariantCulture)
                + " minutes | active deadline " + deadline.ToString("O", CultureInfo.InvariantCulture)
                : " | active budget captured only at future admission")
            + (queue.Entry.Dependency is { } dependency ? " | prerequisite " + dependency.Value.ToString("D") : "")
        : Task?.Question is { } question ? "Question " + question.Key.QuestionId.Value.ToString("D")
            + " | revision " + question.Key.Revision.Value.ToString(CultureInfo.InvariantCulture)
            + " | expires " + question.ExpiresAt.ToString("O", CultureInfo.InvariantCulture) : "No authoritative deadline recorded";
}
