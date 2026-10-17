using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>Content-free work provenance and exact observed records. Stored lifecycle is on the containing session, not inferred from work.</summary>
public sealed record SessionWorkOverviewObservation(
    long AuthorityRevision, DateTimeOffset ObservedAt, long QueueRevision,
    int PendingCount, int PendingCapacity, int ExecutionSlots, int ObservedCurrentFixedReads,
    ImmutableArray<SessionQueueObservation> QueueRecords, int OmittedQueueRecords,
    ImmutableArray<SessionWorkOverviewTask> Tasks, int OmittedTasks,
    ImmutableArray<SessionWorkQuestion> PendingQuestions, int OmittedQuestions)
{
    public SessionQueueDispatchControl? DispatchControl { get; init; }

    public void Validate(HostId<SessionIdentity> session)
    {
        if (AuthorityRevision < 1 || QueueRevision < 0 || ObservedAt.Offset != TimeSpan.Zero
            || PendingCount < 0 || PendingCapacity is < 1 or > SessionQueueLimits.MaximumPendingPerSession
            || ExecutionSlots is < 1 or > SessionQueueLimits.MaximumExecutionSlots || ObservedCurrentFixedReads < 0
            || QueueRecords.IsDefault || Tasks.IsDefault || PendingQuestions.IsDefault
            || Tasks.Length > SessionWorkSnapshot.MaximumRecords || PendingQuestions.Length > SessionWorkSnapshot.MaximumRecords
            || OmittedQueueRecords < 0 || OmittedTasks < 0 || OmittedQuestions < 0)
        {
            throw new InvalidDataException("Work observations require valid revisions, UTC time, bounded metadata and explicit nonnegative omissions.");
        }
        foreach (var record in QueueRecords)
        {
            record.Entry.Validate();
            if (record.Entry.Request.SessionId != session || !Enum.IsDefined(record.Eligibility)
                || record.PendingOrder is < 1 || record.ActiveDeadline is { Offset: var offset } && offset != TimeSpan.Zero)
            {
                throw new InvalidDataException("Observed queue metadata is invalid or belongs to another session.");
            }
        }
        if (ObservedCurrentFixedReads != QueueRecords.Count(record => record.Entry.IsCurrent))
        {
            throw new InvalidDataException("The current fixed-read count must describe the retained queue observation.");
        }
        foreach (var task in Tasks)
        {
            if (task.Request.SessionId != session || task.Generation.Value < 1 || task.Revision.Value < 1
                || !Enum.IsDefined(task.State))
            {
                throw new InvalidDataException("Observed task metadata is invalid or belongs to another session.");
            }
        }
        foreach (var question in PendingQuestions)
        {
            if (question.Key.Request.SessionId != session || question.Generation.Value < 1
                || question.Status != QuestionStatus.Pending || !Enum.IsDefined(question.Kind)
                || question.AnswerChannel is { } channel && !Enum.IsDefined(channel)
                || question.ExpiresAt.Offset != TimeSpan.Zero)
            {
                throw new InvalidDataException("Observed pending-question metadata is invalid or belongs to another session.");
            }
        }
        if (DispatchControl is { } control && (control.RunId == Guid.Empty || control.Generation.Value < 1
            || control.Revision < 0 || control.ControlRevision < 0))
        {
            throw new InvalidDataException("Observed dispatch control metadata is invalid.");
        }
    }
}
