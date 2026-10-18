using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static readonly Guid OverviewTaskA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OverviewRequestA = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OverviewTaskB = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid OverviewRequestB = new("dddddddd-0000-0000-0000-000000000001");

    private static SessionWorkSnapshot CorrelatedOverviewSnapshot(SessionWorkspaceEntry entry, int questions = 0)
    {
        var snapshot = OverviewSnapshot(entry, questions);
        var first = new HostRequest(new(OverviewRequestA), entry.Authority.SessionId, new(OverviewTaskA), RequestOrigin.LocalUi);
        var second = new HostRequest(new(OverviewRequestB), entry.Authority.SessionId, new(OverviewTaskB), RequestOrigin.LocalUi);
        return snapshot with
        {
            Tasks =
            [
                new(new(first, new(1), HostTaskState.Unknown), new(1), "private", false, null),
                new(new(second, new(1), HostTaskState.Succeeded), new(1), "private", true, null)
            ]
        };
    }

    [Theory]
    [MemberData(nameof(AvailabilityCompositions))]
    public async Task RetainedTaskCorrelationAndComposesEveryExistingOverviewPredicate(
        SessionWorkOverviewAvailability? availability, SessionListFilter lifecycle, int queryMode, bool needs,
        SessionWorkOverviewContent content)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 5).Select(id => ListEntry(id, "needle", id != 2)).ToImmutableArray();
        OverviewReader(f, entries);
        f.Logger.Enabled = true;
        f.OverviewReader = (id, _) =>
        {
            var entry = entries.Single(entry => entry.Authority.SessionId == id);
            return entries.IndexOf(entry) switch
            {
                2 => throw new IOException(),
                3 => Task.FromResult(CorrelatedOverviewSnapshot(entry with { Authority = entry.Authority with { Generation = new(2) } })),
                4 => Task.FromResult(OversizedOverviewSnapshot(entry)),
                _ => Task.FromResult(CorrelatedOverviewSnapshot(entry, entry.Authority.IsActive ? 1 : 0))
            };
        };
        var query = queryMode switch
        {
            1 => new SessionListSearch(SessionListSearchKind.NameSubstring, lifecycle, "needle"),
            2 => new SessionListSearch(SessionListSearchKind.ExactId, lifecycle, entries[0].Authority.SessionId.Value.ToString("D")),
            _ => null
        };
        var ranges = new SessionWorkOverviewRanges(AuthorityRevision: new(41, 41), OmittedTasks: new(9, 9));
        var expected = entries.Take(2).Where(entry =>
            (lifecycle == SessionListFilter.All || entry.Authority.IsActive == (lifecycle == SessionListFilter.Active))
            && (query is null || query.Matches(entry))
            && (availability is null or SessionWorkOverviewAvailability.Observed)
            && (!needs || entry.Authority.IsActive)
            && content != SessionWorkOverviewContent.QueueReceipt
            && (content != SessionWorkOverviewContent.PendingQuestion || entry.Authority.IsActive)).ToArray();
        foreach (var correlation in new[] { new SessionWorkOverviewTaskQuery(OverviewTaskA), new(RequestId: OverviewRequestA), new(OverviewTaskA, OverviewRequestA) })
        {
            var page = await f.Service.ReadWorkOverviewAsync(lifecycle, needs, null, f.Token, query, availability, content,
                ranges, HostTaskState.Unknown, correlation);
            page.Records.Select(row => row.Session).Should().Equal(expected);
            page.Scanned.Should().Be(5);
            page.Nonmatches.Should().Be(5 - expected.Length);
            page.Next.Should().BeNull();
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(SessionWorkOverviewPage.MaximumResultBytes);
        }
        f.Logger.Messages.Should().OnlyContain(message => !message.Contains(OverviewTaskA.ToString("D"), StringComparison.Ordinal)
            && !message.Contains(OverviewRequestA.ToString("D"), StringComparison.Ordinal));
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task CrossTaskIDsOrStateNeverJoinAndOmittedOrMalformedSiblingsNeverSupplyAMatch()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var snapshot = CorrelatedOverviewSnapshot(entry);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        var pair = new SessionWorkOverviewTaskQuery(OverviewTaskA, OverviewRequestA);
        var crossIDs = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            taskQuery: new(OverviewTaskA, OverviewRequestB));
        crossIDs.Records.Should().BeEmpty();
        var crossState = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            taskState: HostTaskState.Succeeded, taskQuery: pair);
        crossState.Records.Should().BeEmpty();
        var same = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            taskState: HostTaskState.Unknown, taskQuery: pair);
        same.Records.Should().ContainSingle();
        var first = snapshot.Tasks[0];
        SessionWorkSnapshot[] candidates =
        [
            snapshot with { Tasks = [] },
            snapshot with { Tasks = [first, first with { Generation = default }] },
            snapshot with { Tasks = [first, OverviewSnapshot(ListEntry(2)).Tasks[0]] },
            snapshot with { Tasks = [.. Enumerable.Repeat(first, SessionWorkSnapshot.MaximumRecords + 1)] },
            snapshot with { PendingQuestions = default }
        ];
        foreach (var candidate in candidates)
        {
            f.OverviewReader = (_, _) => Task.FromResult(candidate);
            var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, taskQuery: pair);
            page.Records.Should().BeEmpty();
            page.Scanned.Should().Be(1);
            page.Nonmatches.Should().Be(1);
        }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("task-edit")]
    [InlineData("request-edit")]
    [InlineData("task-clear")]
    [InlineData("request-clear")]
    [InlineData("swap")]
    [InlineData("task-tamper")]
    [InlineData("request-tamper")]
    [InlineData("zero-task")]
    [InlineData("zero-request")]
    public async Task CompleteTaskQueryContinuationIdentityRejectsChangesBeforeReads(string change)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        var query = new SessionWorkOverviewTaskQuery(OverviewTaskA, OverviewRequestA);
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, taskQuery: query);
        first.Records.Should().BeEmpty();
        first.Scanned.Should().Be(10);
        first.Nonmatches.Should().Be(10);
        first.Next!.TaskQuery.Should().Be(query);
        var cursor = first.Next;
        if (change is "task-tamper") { cursor = cursor with { TaskQuery = query with { TaskId = null } }; }
        if (change is "request-tamper") { cursor = cursor with { TaskQuery = query with { RequestId = null } }; }
        var changed = change switch
        {
            "task-edit" => query with { TaskId = OverviewTaskB },
            "request-edit" => query with { RequestId = OverviewRequestB },
            "task-clear" => query with { TaskId = null },
            "request-clear" => query with { RequestId = null },
            "swap" => new(query.RequestId, query.TaskId),
            "zero-task" => query with { TaskId = Guid.Empty },
            "zero-request" => query with { RequestId = Guid.Empty },
            _ => query
        };
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, taskQuery: changed);
        await read.Should().ThrowAsync<Exception>();
        f.ListReads.Should().Be(1);
        var last = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token, taskQuery: query);
        last.Scanned.Should().Be(1);
        last.Nonmatches.Should().Be(1);
        last.Next.Should().BeNull();
        var any = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
        any.Records.Should().HaveCount(10);
        any.Next!.TaskQuery.Should().Be(default(SessionWorkOverviewTaskQuery));
        var added = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, any.Next, f.Token, taskQuery: query);
        await added.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TaskQueryCannotHideCorruptMetadataAnywhereInTheScannedPage()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 10).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.ListReader = (_, _) => new(entries.SetItem(9, entries[9] with { Metadata = entries[0].Metadata }), null);
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.Done, false, null, f.Token,
            taskQuery: new(OverviewTaskA, OverviewRequestA));
        await read.Should().ThrowAsync<InvalidDataException>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelatedCompletePagesDeferWithoutSkippingTheUnconsumedByteBoundHead()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 3).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) =>
        {
            var entry = entries.Single(entry => entry.Authority.SessionId == id);
            return Task.FromResult(OversizedOverviewSnapshot(entry) with
            {
                QueueRecords = [],
                Tasks = [.. Enumerable.Repeat(CorrelatedOverviewSnapshot(entry).Tasks[0], 50)]
            });
        };
        var query = new SessionWorkOverviewTaskQuery(OverviewTaskA, OverviewRequestA);
        SessionWorkOverviewCursor? cursor = null;
        for (var index = 0; index < entries.Length; index++)
        {
            var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, cursor, f.Token,
                taskState: HostTaskState.Unknown, taskQuery: query);
            page.Records.Single().Session.Should().Be(entries[index]);
            page.Scanned.Should().Be(1);
            page.Nonmatches.Should().Be(0);
            page.OutputLimited.Should().Be(index < entries.Length - 1);
            SessionWorkOverviewPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(SessionWorkOverviewPage.MaximumResultBytes);
            cursor = page.Next;
            if (cursor is not null)
            {
                cursor.After.Should().Be(entries[index].Authority.SessionId.Value);
                cursor.TaskQuery.Should().Be(query);
            }
        }
        cursor.Should().BeNull();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }
}
