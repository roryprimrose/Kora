using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewObservationTests
{
    [Fact]
    public void CompleteObservationValidationRejectsMalformedMetadataIndependentlyOfBrowsingFilters()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var now = DateTimeOffset.UnixEpoch;
        var offset = now.ToOffset(TimeSpan.FromHours(1));
        var entry = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending,
            Guid.NewGuid(), 1, now, now.AddMinutes(30));
        var receipt = new SessionQueueObservation(entry, SessionQueueEligibility.Ready, null) { PendingOrder = 1 };
        var question = new SessionWorkQuestion(new(request, new(Guid.NewGuid()), new(1)), new(1),
            QuestionStatus.Pending, QuestionKind.Text, RequestOrigin.LocalUi, now.AddMinutes(30));
        var task = new SessionWorkOverviewTask(request, new(1), new(1), HostTaskState.Succeeded, false);
        var valid = new SessionWorkOverviewObservation(1, now, 0, 1, 10, 1, 0, [receipt], 0, [task], 0, [question], 0);
        valid.Validate(request.SessionId);
        (valid with { QueueRecords = [receipt with { PendingOrder = null }] }).Validate(request.SessionId);
        (valid with
        {
            QueueRecords = [receipt with { Entry = entry.Admit(new(2), 2, now, 5), ActiveDeadline = now.AddMinutes(5) }],
            ObservedCurrentFixedReads = 1,
        }).Validate(request.SessionId);
        var control = new SessionQueueDispatchControl(Guid.NewGuid(), new(1), 0, 1, false, false);
        (valid with { DispatchControl = control }).Validate(request.SessionId);
        (valid with { PendingQuestions = [question with { AnswerChannel = null }] }).Validate(request.SessionId);
        SessionWorkOverviewObservation[] invalid =
        [
            valid with { AuthorityRevision = 0 }, valid with { QueueRevision = -1 }, valid with { ObservedAt = offset },
            valid with { PendingCount = -1 }, valid with { PendingCapacity = 0 }, valid with { PendingCapacity = 11 },
            valid with { ExecutionSlots = 0 }, valid with { ExecutionSlots = 3 }, valid with { ObservedCurrentFixedReads = -1 },
            valid with { QueueRecords = default }, valid with { Tasks = default }, valid with { PendingQuestions = default },
            valid with { Tasks = [.. Enumerable.Repeat(task, 51)] },
            valid with { PendingQuestions = [.. Enumerable.Repeat(question, 51)] },
            valid with { OmittedQueueRecords = -1 }, valid with { OmittedTasks = -1 }, valid with { OmittedQuestions = -1 },
            valid with { QueueRecords = [receipt with { Entry = entry with { Request = HostRequest.Create(RequestOrigin.LocalUi) } }] },
            valid with { QueueRecords = [receipt with { Eligibility = (SessionQueueEligibility)99 }] },
            valid with { QueueRecords = [receipt with { PendingOrder = 0 }] },
            valid with { QueueRecords = [receipt with { ActiveDeadline = offset }] },
            valid with { ObservedCurrentFixedReads = 1 },
            valid with { Tasks = [task with { Request = HostRequest.Create(RequestOrigin.LocalUi) }] },
            valid with { Tasks = [task with { Generation = default }] },
            valid with { Tasks = [task with { Revision = default }] },
            valid with { Tasks = [task with { State = (HostTaskState)99 }] },
            valid with { PendingQuestions = [question with
                { Key = new(HostRequest.Create(RequestOrigin.LocalUi), question.Key.QuestionId, question.Key.Revision) }] },
            valid with { PendingQuestions = [question with { Generation = default }] },
            valid with { PendingQuestions = [question with { Status = QuestionStatus.Answered }] },
            valid with { PendingQuestions = [question with { Kind = (QuestionKind)99 }] },
            valid with { PendingQuestions = [question with { AnswerChannel = (RequestOrigin)99 }] },
            valid with { PendingQuestions = [question with { ExpiresAt = offset }] },
            valid with { DispatchControl = control with { RunId = Guid.Empty } },
            valid with { DispatchControl = control with { Generation = default } },
            valid with { DispatchControl = control with { Revision = -1 } },
            valid with { DispatchControl = control with { ControlRevision = -1 } },
        ];
        foreach (var observation in invalid)
        {
            observation.Invoking(value => value.Validate(request.SessionId)).Should().Throw<InvalidDataException>();
        }
    }
}
