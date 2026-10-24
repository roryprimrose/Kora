using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewFilterTests
{
    [Fact]
    public void StateRequiresAValidatedRetainedTaskAndUnavailableNeverProvesAbsence()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var entry = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), null);
        foreach (var actual in Enum.GetValues<HostTaskState>())
        {
            var work = new SessionWorkOverviewObservation(1, DateTimeOffset.UnixEpoch, 0, 0, 8, 1, 0,
                [], 9, [new(request, new(1), new(1), actual, false)], 9, [], 9);
            foreach (var availability in Enum.GetValues<SessionWorkOverviewAvailability>())
            {
                var row = new SessionWorkOverviewRow(entry, availability, work);
                foreach (var state in Enum.GetValues<HostTaskState>())
                {
                    SessionWorkOverviewFilter.Matches(row, state).Should()
                        .Be(availability == SessionWorkOverviewAvailability.Observed && state == actual);
                    SessionWorkOverviewFilter.Matches(row with { Work = null }, state).Should().BeFalse();
                    SessionWorkOverviewFilter.Matches(row with { Work = work with { Tasks = [] } }, state)
                        .Should().BeFalse("omissions do not supply positive tasks");
                }
                SessionWorkOverviewFilter.Matches(row).Should().BeTrue();
                SessionWorkOverviewFilter.Matches(row with { Work = null }).Should().BeTrue();
            }
        }
        SessionWorkOverviewFilter.ValidateTaskState(null);
        ((Action)(() => SessionWorkOverviewFilter.ValidateTaskState((HostTaskState)99)))
            .Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => SessionWorkOverviewFilter.Matches(new(entry, (SessionWorkOverviewAvailability)99, null))))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => SessionWorkOverviewFilter.Matches(new(entry, SessionWorkOverviewAvailability.ReadUnavailable, null),
            (HostTaskState)(-1)))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CompleteObservationValidationPrecedesStateExclusion()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var entry = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), null);
        var task = new SessionWorkOverviewTask(request, new(1), new(1), HostTaskState.Succeeded, false);
        var work = new SessionWorkOverviewObservation(1, DateTimeOffset.UnixEpoch, 0, 0, 8, 1, 0, [], 0,
            [task], 0, [], 0);
        SessionWorkOverviewObservation[] invalid =
        [
            work with { Tasks = [task, task with { State = (HostTaskState)99 }] },
            work with { Tasks = [task, task with { Request = HostRequest.Create(RequestOrigin.LocalUi) }] },
            work with { Tasks = [task, task with { Generation = default }] },
            work with { Tasks = [task, task with { Revision = default }] },
            work with { Tasks = [.. Enumerable.Repeat(task, SessionWorkSnapshot.MaximumRecords + 1)] },
            work with { OmittedTasks = -1 }, work with { OmittedQuestions = -1 },
            work with { Tasks = default }, work with { PendingQuestions = default }
        ];
        foreach (var observation in invalid)
        {
            var row = new SessionWorkOverviewRow(entry, SessionWorkOverviewAvailability.ReadUnavailable, observation);
            ((Action)(() => SessionWorkOverviewFilter.Matches(row, HostTaskState.Failed)))
                .Should().Throw<InvalidDataException>();
        }
    }
}
