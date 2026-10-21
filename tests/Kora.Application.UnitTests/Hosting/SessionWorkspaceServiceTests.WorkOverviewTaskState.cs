using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("edit")]
    [InlineData("tamper")]
    [InlineData("clear")]
    [InlineData("undefined")]
    public async Task TaskStateContinuationMismatchFailsBeforeAnyReadAndZeroMatchesAdvance(string change)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, taskState: HostTaskState.Failed);
        first.Records.Should().BeEmpty();
        first.Scanned.Should().Be(10);
        first.Nonmatches.Should().Be(10);
        first.Next!.TaskState.Should().Be(HostTaskState.Failed);
        var cursor = change is "tamper" ? first.Next with { TaskState = null } : first.Next;
        HostTaskState? state = change switch
        {
            "edit" => HostTaskState.Unknown,
            "clear" => null,
            "undefined" => (HostTaskState)99,
            _ => HostTaskState.Failed
        };
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, taskState: state);
        await read.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(1);
        var last = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token, taskState: HostTaskState.Failed);
        last.Scanned.Should().Be(1);
        last.Nonmatches.Should().Be(1);
        last.Next.Should().BeNull();
        var any = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        any.Records.Should().HaveCount(10);
        any.Next!.TaskState.Should().BeNull();
        var added = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, any.Next, f.Token, taskState: HostTaskState.Failed);
        await added.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TaskStateNeverUsesOmissionsAndRejectsAnEntireMalformedObservationBeforeExclusion()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var snapshot = OverviewSnapshot(entry);
        var task = snapshot.Tasks[0];
        SessionWorkSnapshot[] invalid =
        [
            snapshot with { Tasks = [task, OverviewSnapshot(ListEntry(2)).Tasks[0]] },
            snapshot with { Tasks = [task, task with { Generation = default }] },
            snapshot with { Tasks = [.. Enumerable.Repeat(task, SessionWorkSnapshot.MaximumRecords + 1)] },
            snapshot with { PendingQuestions = default }, snapshot with { OmittedTasks = -1 }
        ];
        foreach (var candidate in invalid)
        {
            f.OverviewReader = (_, _) => Task.FromResult(candidate);
            var positive = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, taskState: task.Task.State);
            positive.Records.Should().BeEmpty("even a matching first task cannot hide malformed later records");
            var any = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
            any.Records.Single().Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
            any.Records.Single().Work.Should().BeNull();
        }
        f.OverviewReader = (_, _) => Task.FromResult(snapshot with { Tasks = [], OmittedTasks = 9 });
        var omitted = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, taskState: HostTaskState.Unknown);
        omitted.Records.Should().BeEmpty();
        var unfiltered = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        unfiltered.Records.Single().Work!.OmittedTasks.Should().Be(9);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }
}
