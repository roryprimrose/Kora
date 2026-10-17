using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [MemberData(nameof(AvailabilityCompositions))]
    public async Task WorkRangesAndComposeWithLifecycleBrowseNameExactIdNeedsAnswerAvailabilityAndPositiveContent(
        SessionWorkOverviewAvailability? availability, SessionListFilter lifecycle, int queryMode, bool needs,
        SessionWorkOverviewContent content)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 5).Select(id => ListEntry(id, "needle", id != 2)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) =>
        {
            var entry = entries.Single(entry => entry.Authority.SessionId == id);
            return entries.IndexOf(entry) switch
            {
                2 => throw new IOException(),
                3 => Task.FromResult(OverviewSnapshot(entry with { Authority = entry.Authority with { Generation = new(2) } })),
                4 => Task.FromResult(OversizedOverviewSnapshot(entry)),
                _ => Task.FromResult(OverviewSnapshot(entry, entry.Authority.IsActive ? 1 : 0))
            };
        };
        var query = queryMode switch
        {
            1 => new SessionListSearch(SessionListSearchKind.NameSubstring, lifecycle, "needle"),
            2 => new SessionListSearch(SessionListSearchKind.ExactId, lifecycle, entries[0].Authority.SessionId.Value.ToString("D")),
            _ => null
        };
        var ranges = new SessionWorkOverviewRanges(new(41, 41), new(37, 37), new(0, 0),
            new(8, 8), new(2, 2), new(0, 0), new(4, 4), new(9, 9), new(0, 0));
        var expected = entries.Take(2).Where(entry =>
            (lifecycle == SessionListFilter.All || entry.Authority.IsActive == (lifecycle == SessionListFilter.Active))
            && (query is null || query.Matches(entry))
            && (availability is null or SessionWorkOverviewAvailability.Observed)
            && (!needs || entry.Authority.IsActive)
            && content != SessionWorkOverviewContent.QueueReceipt
            && (content != SessionWorkOverviewContent.PendingQuestion || entry.Authority.IsActive)).ToArray();
        var page = await f.Service.ReadWorkOverviewAsync(lifecycle, needs, null, f.Token, query, availability, content, ranges);
        page.Records.Select(row => row.Session).Should().Equal(expected);
        page.Scanned.Should().Be(5);
        page.Nonmatches.Should().Be(5 - expected.Length);
        page.Next.Should().BeNull();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task BothEndpointsOfEveryRangeBindContinuationAndEmptyPagesStillAdvance(int index)
    {
        foreach (var maximum in new[] { false, true })
        {
            using var f = new Fixture();
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
            var range = maximum ? new SessionWorkMetadataRange(null, 100) : new(100, null);
            var ranges = OverviewRanges(index, range);
            var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token, ranges: ranges);
            first.Scanned.Should().Be(10);
            first.Next!.Ranges.Should().Be(ranges);
            if (!maximum)
            {
                first.Records.Should().BeEmpty();
                first.Nonmatches.Should().Be(10);
            }
            foreach (var replacement in new[] { default, OverviewRanges(index, maximum ? new(null, 99) : new(101, null)) })
            {
                var changed = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token, ranges: replacement);
                await changed.Should().ThrowAsync<ArgumentException>();
                var tampered = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false,
                    first.Next with { Ranges = replacement }, f.Token, ranges: ranges);
                await tampered.Should().ThrowAsync<ArgumentException>();
            }
            var added = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false,
                first.Next with { Ranges = default }, f.Token, ranges: ranges);
            await added.Should().ThrowAsync<ArgumentException>();
            f.ListReads.Should().Be(1);
            var last = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token, ranges: ranges);
            last.Scanned.Should().Be(1);
            last.Next.Should().BeNull();
        }
    }

    [Fact]
    public async Task MalformedRangeAdmissionIsRejectedBeforeMetadataReadsAndInvalidObservationsCannotHideBehindNonmatches()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        var invalidQuery = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            ranges: new(AuthorityRevision: new(0, null)));
        await invalidQuery.Should().ThrowAsync<InvalidDataException>();
        f.ListReads.Should().Be(0);
        var snapshot = OverviewSnapshot(entry);
        SessionWorkSnapshot[] malformed =
        [
            snapshot with { AuthorityRevision = 0 }, snapshot with { Queue = snapshot.Queue with { Revision = -1 } },
            snapshot with { PendingCount = -1 }, snapshot with { PendingCapacity = 0 },
            snapshot with { ExecutionSlots = 0 }, snapshot with { OmittedQueueRecords = -1 },
            snapshot with { OmittedTasks = -1 }, snapshot with { OmittedQuestions = -1 },
            snapshot with { Tasks = default }, snapshot with { QueueRecords = default },
            snapshot with { PendingQuestions = default },
            snapshot with { Tasks = [snapshot.Tasks[0] with { Generation = default }] }
        ];
        foreach (var candidate in malformed)
        {
            f.OverviewReader = (_, _) => Task.FromResult(candidate);
            var excluded = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
                ranges: new(AuthorityRevision: new(long.MaxValue, null)));
            excluded.Records.Should().BeEmpty();
            var actual = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token,
                availability: SessionWorkOverviewAvailability.ReadUnavailable);
            actual.Records.Single().Work.Should().BeNull("the entire observation is rejected, not merely a nonmatching field");
        }
    }

    [Fact]
    public async Task OmissionRangesMatchExplicitCountsButCannotSupplyPositiveObservedContent()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) => Task.FromResult(OverviewSnapshot(entry) with
        {
            Tasks = [], OmittedTasks = 7, OmittedQueueRecords = 8, OmittedQuestions = 9
        });
        var ranges = new SessionWorkOverviewRanges(OmittedQueueRecords: new(8, 8),
            OmittedTasks: new(7, 7), OmittedQuestions: new(9, 9));
        var any = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token, ranges: ranges);
        any.Records.Single().Work!.OmittedQuestions.Should().Be(9);
        foreach (var content in Enum.GetValues<SessionWorkOverviewContent>().Skip(1))
        {
            var positive = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token,
                content: content, ranges: ranges);
            positive.Records.Should().BeEmpty();
            positive.Nonmatches.Should().Be(1);
        }
    }

    [Fact]
    public async Task RangedCompletePagesPreserveByteDeferralAndNeverMatchOutputBoundProjections()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 3).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) =>
        {
            var snapshot = OversizedOverviewSnapshot(entries.Single(entry => entry.Authority.SessionId == id));
            return Task.FromResult(id == entries[2].Authority.SessionId ? snapshot : snapshot with { QueueRecords = [] });
        };
        var ranges = new SessionWorkOverviewRanges(OmittedTasks: new(9, 9));
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token, ranges: ranges);
        first.OutputLimited.Should().BeTrue();
        first.Scanned.Should().Be(1);
        first.Next!.After.Should().Be(entries[0].Authority.SessionId.Value);
        var second = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, first.Next, f.Token, ranges: ranges);
        second.Records.Single().Session.Should().Be(entries[1]);
        second.Scanned.Should().Be(2);
        second.Nonmatches.Should().Be(1, "OutputBound does not match even when the preclassification metadata would match");
        second.Next.Should().BeNull();
        SessionWorkOverviewPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(65536);
        SessionWorkOverviewPage.Serialize(second).Length.Should().BeLessThanOrEqualTo(65536);
    }

    private static SessionWorkOverviewRanges OverviewRanges(int index, SessionWorkMetadataRange range) => index switch
    {
        0 => new(AuthorityRevision: range), 1 => new(QueueRevision: range), 2 => new(PendingCount: range),
        3 => new(PendingCapacity: range), 4 => new(ExecutionSlots: range), 5 => new(ObservedCurrentFixedReads: range),
        6 => new(OmittedQueueRecords: range), 7 => new(OmittedTasks: range), _ => new(OmittedQuestions: range)
    };
}
