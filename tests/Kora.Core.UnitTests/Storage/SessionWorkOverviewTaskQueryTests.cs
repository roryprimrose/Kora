using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewTaskQueryTests
{
    private static readonly Guid TaskA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid RequestA = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TaskB = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid RequestB = new("dddddddd-0000-0000-0000-000000000001");

    [Fact]
    public void IDsAndStateMustMatchTheSameValidatedRetainedTaskAndPreserveAny()
    {
        var session = new HostId<SessionIdentity>(new("eeeeeeee-0000-0000-0000-000000000001"));
        var entry = new SessionWorkspaceEntry(new(session, new(1), true), null);
        var first = new SessionWorkOverviewTask(new(new(RequestA), session, new(TaskA), RequestOrigin.LocalUi),
            new(1), new(1), HostTaskState.Unknown, false);
        var second = new SessionWorkOverviewTask(new(new(RequestB), session, new(TaskB), RequestOrigin.LocalUi),
            new(1), new(1), HostTaskState.Succeeded, true);
        var work = new SessionWorkOverviewObservation(1, DateTimeOffset.UnixEpoch, 0, 0, 8, 1, 0, [], 9,
            [first, second], 9, [], 9);
        Guid?[] tasks = [null, TaskA, TaskB, RequestA];
        Guid?[] requests = [null, RequestA, RequestB, TaskA];
        HostTaskState?[] states = [null, .. Enum.GetValues<HostTaskState>()];
        foreach (var availability in Enum.GetValues<SessionWorkOverviewAvailability>())
        {
            foreach (var task in tasks)
            {
                foreach (var request in requests)
                {
                    foreach (var state in states)
                    {
                        var query = new SessionWorkOverviewTaskQuery(task, request);
                        var constrained = task is not null || request is not null || state is not null;
                        var expected = !constrained || availability == SessionWorkOverviewAvailability.Observed
                            && ((task is null || task == TaskA) && (request is null || request == RequestA)
                                    && (state is null || state == HostTaskState.Unknown)
                                || (task is null || task == TaskB) && (request is null || request == RequestB)
                                    && (state is null || state == HostTaskState.Succeeded));
                        var row = new SessionWorkOverviewRow(entry, availability, work);
                        SessionWorkOverviewFilter.Matches(null, row, taskState: state, taskQuery: query).Should().Be(expected);
                        SessionWorkOverviewFilter.Matches(availability, row, taskState: state, taskQuery: query).Should().Be(expected);
                        SessionWorkOverviewFilter.Matches(null, row with { Work = null }, taskState: state, taskQuery: query)
                            .Should().Be(!constrained);
                        SessionWorkOverviewFilter.Matches(null, row with { Work = work with { Tasks = [] } },
                            taskState: state, taskQuery: query).Should().Be(!constrained, "omissions are not retained task records");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" aaaaaaaa-0000-0000-0000-000000000001")]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001 ")]
    [InlineData("AAAAAAAA-0000-0000-0000-000000000001")]
    [InlineData("aaaaaaaa000000000000000000000001")]
    [InlineData("{aaaaaaaa-0000-0000-0000-000000000001}")]
    [InlineData("(aaaaaaaa-0000-0000-0000-000000000001)")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public void TextAdmissionRejectsInvalidIDsWithoutRepair(string text)
    {
        var task = () => SessionWorkOverviewTaskQuery.Parse(text, "");
        task.Should().Throw<InvalidDataException>();
        var request = () => SessionWorkOverviewTaskQuery.Parse("", text);
        request.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void TypedAndTextAdmissionShareCanonicalRulesAndWholeObservationValidationPrecedesExclusion()
    {
        SessionWorkOverviewTaskQuery.Parse("", "").Should().Be(default(SessionWorkOverviewTaskQuery));
        SessionWorkOverviewTaskQuery.Parse(TaskA.ToString("D"), RequestA.ToString("D")).Should().Be(new SessionWorkOverviewTaskQuery(TaskA, RequestA));
        var invalidTask = () => new SessionWorkOverviewTaskQuery(Guid.Empty).Validate();
        invalidTask.Should().Throw<InvalidDataException>();
        var invalidRequest = () => new SessionWorkOverviewTaskQuery(RequestId: Guid.Empty).Validate();
        invalidRequest.Should().Throw<InvalidDataException>();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var entry = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), null);
        var task = new SessionWorkOverviewTask(request, new(1), new(1), HostTaskState.Unknown, true);
        var work = new SessionWorkOverviewObservation(1, DateTimeOffset.UnixEpoch, 0, 0, 8, 1, 0, [], 0,
            [task, task with { Revision = default }], 0, [], 0);
        foreach (var query in new[] { new SessionWorkOverviewTaskQuery(request.TaskId.Value, request.RequestId.Value), new(TaskA, RequestB) })
        {
            var match = () => SessionWorkOverviewFilter.Matches(SessionWorkOverviewAvailability.ReadUnavailable,
                new(entry, SessionWorkOverviewAvailability.Observed, work), taskState: HostTaskState.Failed, taskQuery: query);
            match.Should().Throw<InvalidDataException>("matching or nonmatching first tasks cannot hide malformed later records");
        }
    }
}
