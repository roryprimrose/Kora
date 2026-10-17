using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewFilterTests
{
    [Fact]
    public void ExactTaskStateRequiresAValidatedRetainedTaskAndAndComposesEveryPositiveChoice()
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
                    foreach (var content in Enum.GetValues<SessionWorkOverviewContent>())
                    {
                        var expected = availability == SessionWorkOverviewAvailability.Observed && state == actual
                            && content is SessionWorkOverviewContent.Any or SessionWorkOverviewContent.Task;
                        SessionWorkOverviewFilter.Matches(null, row, content, state).Should().Be(expected);
                        SessionWorkOverviewFilter.Matches(availability, row, content, state).Should().Be(expected);
                        SessionWorkOverviewFilter.Matches(null, row with { Work = null }, content, state).Should().BeFalse();
                        SessionWorkOverviewFilter.Matches(null, row with { Work = work with { Tasks = [] } }, content, state)
                            .Should().BeFalse("omissions do not supply positive tasks");
                    }
                }
                SessionWorkOverviewFilter.Matches(null, row).Should().BeTrue("Any preserves the existing default");
            }
        }
        var invalid = () => SessionWorkOverviewFilter.Validate((HostTaskState)99);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var invalidMatch = () => SessionWorkOverviewFilter.Matches(null,
            new(entry, SessionWorkOverviewAvailability.ReadUnavailable, null), taskState: (HostTaskState)(-1));
        invalidMatch.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CompleteObservationValidationPrecedesEvenAvailabilityAndStateExclusion()
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
            var row = new SessionWorkOverviewRow(entry, SessionWorkOverviewAvailability.Observed, observation);
            var match = () => SessionWorkOverviewFilter.Matches(SessionWorkOverviewAvailability.ReadUnavailable, row,
                SessionWorkOverviewContent.QueueReceipt, HostTaskState.Failed);
            match.Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void PositiveContentRequiresRetainedObservedRecordsRegardlessOfOmissionsOrOtherContent()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var entry = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), null);
        var now = DateTimeOffset.UnixEpoch;
        var receipt = new SessionQueueObservation(new(request, new(1), new(1), 1,
            SessionQueueState.Pending, Guid.NewGuid(), 1, now, now.AddMinutes(30)), SessionQueueEligibility.Ready, null);
        var task = new SessionWorkOverviewTask(request, new(1), new(1), HostTaskState.Unknown, false);
        var question = new SessionWorkQuestion(new(request, new(Guid.NewGuid()), new(1)), new(1),
            Kora.Core.Interaction.QuestionStatus.Pending, Kora.Core.Interaction.QuestionKind.SingleChoice,
            RequestOrigin.LocalUi, now.AddMinutes(30));
        foreach (var actual in Enum.GetValues<SessionWorkOverviewAvailability>())
        {
            for (var mask = 0; mask < 8; mask++)
            {
                var work = new SessionWorkOverviewObservation(1, now, 1, 0, 8, 1, 0,
                    (mask & 1) != 0 ? [receipt] : [], 7, (mask & 2) != 0 ? [task] : [], 7,
                    (mask & 4) != 0 ? [question] : [], 7);
                var row = new SessionWorkOverviewRow(entry, actual, work);
                foreach (var content in Enum.GetValues<SessionWorkOverviewContent>())
                {
                    var expected = content == SessionWorkOverviewContent.Any
                        || actual == SessionWorkOverviewAvailability.Observed && (mask & (1 << ((int)content - 1))) != 0;
                    SessionWorkOverviewFilter.Matches(null, row, content).Should().Be(expected);
                    SessionWorkOverviewFilter.Matches(actual, row, content).Should().Be(expected);
                    SessionWorkOverviewFilter.Matches(null, row with { Work = null }, content).Should()
                        .Be(content == SessionWorkOverviewContent.Any);
                }
            }
        }
        var invalid = () => SessionWorkOverviewFilter.Matches(null,
            new(entry, SessionWorkOverviewAvailability.Observed, null), (SessionWorkOverviewContent)99);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AvailabilityIsExactAndContentFreeForEveryDefinedValueAndAny()
    {
        var id = new HostId<SessionIdentity>(Guid.NewGuid());
        var entry = new SessionWorkspaceEntry(new(id, new(1), true), null);
        foreach (var actual in Enum.GetValues<SessionWorkOverviewAvailability>())
        {
            var row = new SessionWorkOverviewRow(entry, actual, null);
            SessionWorkOverviewFilter.Matches(null, row).Should().BeTrue();
            foreach (var filter in Enum.GetValues<SessionWorkOverviewAvailability>())
            {
                SessionWorkOverviewFilter.Matches(filter, row).Should().Be(filter == actual);
            }
        }
        var invalidFilter = () => SessionWorkOverviewFilter.Validate((SessionWorkOverviewAvailability)99);
        invalidFilter.Should().Throw<ArgumentOutOfRangeException>();
        var invalidMatch = () => SessionWorkOverviewFilter.Matches((SessionWorkOverviewAvailability)99,
            new(entry, SessionWorkOverviewAvailability.Observed, null));
        invalidMatch.Should().Throw<ArgumentOutOfRangeException>();
        foreach (var filter in new SessionWorkOverviewAvailability?[] { null, SessionWorkOverviewAvailability.Observed })
        {
            var invalidRow = () => SessionWorkOverviewFilter.Matches(filter, new(entry, (SessionWorkOverviewAvailability)99, null));
            invalidRow.Should().Throw<InvalidDataException>();
        }
    }
}
