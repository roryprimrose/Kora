using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    public static TheoryData<SessionWorkOverviewAvailability?, SessionListFilter, int, bool, SessionWorkOverviewContent> AvailabilityCompositions
    {
        get
        {
            var data = new TheoryData<SessionWorkOverviewAvailability?, SessionListFilter, int, bool, SessionWorkOverviewContent>();
            foreach (var availability in new SessionWorkOverviewAvailability?[] { null,
                SessionWorkOverviewAvailability.Observed, SessionWorkOverviewAvailability.SourceChanged,
                SessionWorkOverviewAvailability.ReadUnavailable, SessionWorkOverviewAvailability.OutputBound })
            {
                foreach (var lifecycle in Enum.GetValues<SessionListFilter>())
                {
                    foreach (var query in new[] { 0, 1, 2 })
                    {
                        foreach (var needs in new[] { false, true })
                        {
                            foreach (var content in Enum.GetValues<SessionWorkOverviewContent>())
                            {
                                data.Add(availability, lifecycle, query, needs, content);
                            }
                        }
                    }
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AvailabilityCompositions))]
    public async Task ExactAvailabilityComposesWithEveryLifecycleQueryAndNeedsAnswerWithoutEmptyWorkInference(
        SessionWorkOverviewAvailability? availability, SessionListFilter lifecycle, int queryMode, bool needs,
        SessionWorkOverviewContent content)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 8).Select(id => ListEntry(id, "needle", id <= 4)).ToImmutableArray();
        OverviewReader(f, entries);
        var reads = 0;
        f.OverviewReader = (id, _) =>
        {
            reads++;
            var entry = entries.Single(entry => entry.Authority.SessionId == id);
            var index = entries.IndexOf(entry) % 4;
            var snapshot = OverviewSnapshot(entry, entry.Authority.IsActive ? 1 : 0);
            if (entry.Authority.IsActive)
            {
                var queue = new SessionQueueEntry(snapshot.Tasks[0].Task.Request, new(1), new(1), 1,
                    SessionQueueState.Pending, Guid.NewGuid(), 1, snapshot.ObservedAt, snapshot.ObservedAt.AddMinutes(30));
                snapshot = snapshot with { QueueRecords = [new(queue, SessionQueueEligibility.Ready, null)] };
            }
            return index switch
            {
                0 => Task.FromResult(snapshot),
                1 => Task.FromResult(OverviewSnapshot(entry with { Authority = entry.Authority with { Generation = new(2) } })),
                2 => throw new IOException("Unavailable exact observation."),
                _ => Task.FromResult(OversizedOverviewSnapshot(entry))
            };
        };
        var query = queryMode switch
        {
            1 => new SessionListSearch(SessionListSearchKind.NameSubstring, lifecycle, "needle"),
            2 => new SessionListSearch(SessionListSearchKind.ExactId, lifecycle, entries[3].Authority.SessionId.Value.ToString("D")),
            _ => null
        };
        var expected = entries.Where(entry =>
            (lifecycle == SessionListFilter.All || entry.Authority.IsActive == (lifecycle == SessionListFilter.Active))
            && (query is null || query.Matches(entry))).ToArray();
        var expectedReads = expected.Length;
        expected = expected.Where(entry =>
        {
            var actual = (SessionWorkOverviewAvailability)(entries.IndexOf(entry) % 4);
            return (availability is null || availability == actual)
                && (!needs || actual != SessionWorkOverviewAvailability.Observed || entry.Authority.IsActive)
                && (content == SessionWorkOverviewContent.Any || actual == SessionWorkOverviewAvailability.Observed
                    && (content == SessionWorkOverviewContent.Task || entry.Authority.IsActive));
        }).ToArray();
        var page = await f.Service.ReadWorkOverviewAsync(lifecycle, needs, null, f.Token, query, availability, content);
        page.Records.Select(row => row.Session).Should().Equal(expected);
        page.Scanned.Should().Be(8);
        page.Nonmatches.Should().Be(8 - expected.Length);
        page.Next.Should().BeNull();
        reads.Should().Be(expectedReads, "availability exclusions happen only after the admitted work read");
        f.ControlCalls.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("tamper")]
    [InlineData("undefined")]
    public async Task ContentContinuationIsExactAndZeroMatchesAdvanceWithoutAdditionalObservation(string change)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token,
            content: SessionWorkOverviewContent.PendingQuestion);
        first.Records.Should().BeEmpty("question omissions and other records are not positive observed questions");
        first.Nonmatches.Should().Be(10);
        first.Next!.Content.Should().Be(SessionWorkOverviewContent.PendingQuestion);
        var cursor = change is "tamper" ? first.Next with { Content = SessionWorkOverviewContent.Any } : first.Next;
        var content = change is "edit" ? SessionWorkOverviewContent.Task
            : change is "undefined" ? (SessionWorkOverviewContent)99 : SessionWorkOverviewContent.PendingQuestion;
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, cursor, f.Token, content: content);
        await read.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(1);
        var last = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, first.Next, f.Token,
            content: SessionWorkOverviewContent.PendingQuestion);
        last.Records.Should().BeEmpty();
        last.Scanned.Should().Be(1);
        last.Next.Should().BeNull();
        f.ControlCalls.Should().Be(0);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SessionWorkOverviewContent.QueueReceipt)]
    [InlineData(SessionWorkOverviewContent.Task)]
    [InlineData(SessionWorkOverviewContent.PendingQuestion)]
    public async Task OmittedOnlyContentIsNotAPositiveMatchAndDoesNotClaimAbsence(SessionWorkOverviewContent content)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) => Task.FromResult(OverviewSnapshot(entry) with
        {
            Tasks = [], OmittedTasks = 7, OmittedQueueRecords = 8, OmittedQuestions = 9
        });
        var positive = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token, content: content);
        positive.Records.Should().BeEmpty();
        positive.Nonmatches.Should().Be(1);
        var any = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token);
        var work = any.Records.Single().Work!;
        work.OmittedTasks.Should().Be(7);
        work.OmittedQueueRecords.Should().Be(8);
        work.OmittedQuestions.Should().Be(9);
        SessionWorkOverviewPage.Scope.Should().Contain("No full-scope totals or all-clear");
    }

    private static SessionWorkSnapshot OversizedOverviewSnapshot(SessionWorkspaceEntry entry)
    {
        var snapshot = OverviewSnapshot(entry, 50);
        var queue = new SessionQueueEntry(snapshot.Tasks[0].Task.Request, new(1), new(1), 1,
            SessionQueueState.Pending, Guid.NewGuid(), 1, snapshot.ObservedAt, snapshot.ObservedAt.AddMinutes(30));
        return snapshot with { Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], 50)],
            QueueRecords = [.. Enumerable.Repeat(new SessionQueueObservation(queue, SessionQueueEligibility.Ready, null), 50)] };
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("tamper")]
    [InlineData("undefined")]
    public async Task AvailabilityContinuationChangesAreRefusedBeforeReading(string change)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        OverviewReader(f, [.. Enumerable.Range(1, 11).Select(id => ListEntry(id))]);
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            availability: SessionWorkOverviewAvailability.ReadUnavailable);
        first.Records.Should().BeEmpty();
        first.Nonmatches.Should().Be(10);
        first.Next!.Availability.Should().Be(SessionWorkOverviewAvailability.ReadUnavailable);
        var cursor = change is "tamper" ? first.Next with { Availability = null } : first.Next;
        var availability = change is "edit" ? SessionWorkOverviewAvailability.Observed
            : change is "undefined" ? (SessionWorkOverviewAvailability)99 : SessionWorkOverviewAvailability.ReadUnavailable;
        var read = () => f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, cursor, f.Token, availability: availability);
        await read.Should().ThrowAsync<ArgumentException>();
        f.ListReads.Should().Be(1);
        var last = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, first.Next, f.Token,
            availability: SessionWorkOverviewAvailability.ReadUnavailable);
        last.Records.Should().BeEmpty();
        last.Scanned.Should().Be(1);
        last.Next.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcludedOrByteDeferredMetadataStillRequiresFullValidation(bool deferred)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 4).Select(id => ListEntry(id)).ToImmutableArray();
        entries = entries.SetItem(3, entries[3] with { Metadata = entries[0].Metadata });
        OverviewReader(f, entries);
        f.OverviewReader = (_, _) => throw new InvalidOperationException("Metadata validation must finish first.");
        var query = deferred ? null : new SessionListSearch(SessionListSearchKind.NameSubstring, SessionListFilter.Done, "absent");
        var read = () => f.Service.ReadWorkOverviewAsync(deferred ? SessionListFilter.All : SessionListFilter.Done,
            true, null, f.Token, query, SessionWorkOverviewAvailability.OutputBound);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task MatchingCompleteObservationsDeferWithoutConsumingTheByteBoundHead()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entries = Enumerable.Range(1, 3).Select(id => ListEntry(id)).ToImmutableArray();
        OverviewReader(f, entries);
        f.OverviewReader = (id, _) => Task.FromResult(OversizedOverviewSnapshot(
            entries.Single(entry => entry.Authority.SessionId == id)) with { QueueRecords = [] });
        var first = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token,
            availability: SessionWorkOverviewAvailability.Observed);
        first.OutputLimited.Should().BeTrue();
        first.Records.Should().ContainSingle();
        first.Scanned.Should().Be(1);
        first.Next!.After.Should().Be(entries[0].Authority.SessionId.Value);
        first.Next.Availability.Should().Be(SessionWorkOverviewAvailability.Observed);
        var second = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, first.Next, f.Token,
            availability: SessionWorkOverviewAvailability.Observed);
        second.Records.Single().Session.Should().Be(entries[1]);
        second.Next!.After.Should().Be(entries[1].Authority.SessionId.Value);
        var third = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, second.Next, f.Token,
            availability: SessionWorkOverviewAvailability.Observed);
        third.Records.Single().Session.Should().Be(entries[2]);
        third.Next.Should().BeNull();
        third.OutputLimited.Should().BeFalse();
        SessionWorkOverviewPage.Serialize(first).Length.Should().BeLessThanOrEqualTo(SessionWorkOverviewPage.MaximumResultBytes);
    }

    [Fact]
    public async Task AvailabilityExclusionStillValidatesForeignAndUnboundedObservationRows()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var entry = ListEntry(1);
        OverviewReader(f, [entry]);
        f.OverviewReader = (_, _) => Task.FromResult(OverviewSnapshot(entry) with { Tasks = OverviewSnapshot(ListEntry(2)).Tasks });
        var page = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token,
            availability: SessionWorkOverviewAvailability.Observed);
        page.Records.Should().BeEmpty();
        page.Nonmatches.Should().Be(1);
        var unavailable = await f.Service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, f.Token,
            availability: SessionWorkOverviewAvailability.ReadUnavailable);
        unavailable.Records.Single().Work.Should().BeNull();
    }
}
