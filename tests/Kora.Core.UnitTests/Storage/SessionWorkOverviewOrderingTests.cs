using System.Collections.Immutable;
using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewOrderingTests
{
    [Fact]
    public void EveryOrderUsesOnlyTheLoadedMetadataAndPreservesRawPageBytesAndContinuation()
    {
        var rows = new[] { Row(5, null), Row(3, "z"), Row(2, "A"), Row(1, "A"), Row(4, "a") };
        rows[0] = rows[0] with { Availability = SessionWorkOverviewAvailability.ReadUnavailable, Work = null };
        rows[1] = rows[1] with { Availability = SessionWorkOverviewAvailability.SourceChanged, Work = null };
        var cursor = new SessionWorkOverviewCursor(Guid.NewGuid(), 7, 3, rows[0].Session.Authority.SessionId.Value,
            SessionListFilter.All, false, null);
        var page = new SessionWorkOverviewPage([.. rows], 7, 2, true, cursor);
        var bytes = SessionWorkOverviewPage.Serialize(page);
        SessionWorkOverviewOrdering.Project(page).Should().Equal(rows);
        int[][] expected = [[5, 3, 2, 1, 4], [1, 2, 3, 4, 5], [5, 4, 3, 2, 1],
            [1, 2, 4, 3, 5], [1, 2, 4, 3, 5], [3, 4, 1, 2, 5]];
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR", "sv-SE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var order in Enum.GetValues<SessionWorkOverviewPageOrder>())
                {
                    var projection = SessionWorkOverviewOrdering.Project(page, order);
                    projection.Should().Equal(expected[(int)order].Select(index => rows.Single(row =>
                        row.Session.Authority.SessionId == Row(index, null).Session.Authority.SessionId)));
                    projection.Should().OnlyContain(row => rows.Contains(row));
                    SessionWorkOverviewPage.Serialize(page).Should().Equal(bytes);
                    page.Next.Should().BeSameAs(cursor);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        var empty = new SessionWorkOverviewPage([], 0, 0, false, null);
        foreach (var order in Enum.GetValues<SessionWorkOverviewPageOrder>())
        {
            SessionWorkOverviewOrdering.Project(empty, order).Should().BeEmpty();
        }
        var unnamed = new SessionWorkOverviewPage([Row(2, null), Row(1, null)], 2, 0, false, null);
        SessionWorkOverviewOrdering.Project(unnamed, SessionWorkOverviewPageOrder.SessionNameAscending)
            .Should().Equal(unnamed.Records.Reverse());
        SessionWorkOverviewOrdering.Project(unnamed, SessionWorkOverviewPageOrder.SessionNameDescending)
            .Should().Equal(unnamed.Records.Reverse());
    }

    [Fact]
    public void EveryModeRejectsTheCompleteCorruptPageIncludingTheLastRowBeforeReturningAnything()
    {
        var row = Row(1, "Admitted");
        var last = Row(2, null);
        var page = new SessionWorkOverviewPage([row, last], 2, 0, false, null);
        SessionWorkOverviewRow[] invalidRows =
        [
            null!,
            last with { Session = null! },
            last with { Availability = (SessionWorkOverviewAvailability)99 },
            last with { Work = null },
            last with { Availability = SessionWorkOverviewAvailability.OutputBound },
            last with { Work = last.Work! with { AuthorityRevision = 0 } },
            last with { Session = last.Session with { Authority = null! } },
            last with { Session = last.Session with { Authority = last.Session.Authority with { SessionId = default } } },
            last with { Session = last.Session with { Authority = last.Session.Authority with { Generation = default } } },
            last with { Session = last.Session with { Metadata = new(row.Session.Authority.SessionId, new(1), new("Other")) } },
            last with { Session = last.Session with { Metadata = new(last.Session.Authority.SessionId, default, new("Other")) } },
            last with { Session = last.Session with { Metadata = new(last.Session.Authority.SessionId, new(1), null!) } },
            row
        ];
        SessionWorkOverviewPage[] invalidPages =
        [
            page with { Records = default },
            page with { Records = [.. Enumerable.Repeat(row, 11)] },
            page with { Scanned = -1 },
            page with { Scanned = 11 },
            page with { Nonmatches = -1 },
            page with { Nonmatches = 3 },
            page with { Nonmatches = 1 },
            .. invalidRows.Select(invalid => page with { Records = [row, invalid] })
        ];
        foreach (var order in Enum.GetValues<SessionWorkOverviewPageOrder>())
        {
            foreach (var invalid in invalidPages)
            {
                var project = () => SessionWorkOverviewOrdering.Project(invalid, order);
                project.Should().Throw<Exception>();
            }
        }
        var missing = () => SessionWorkOverviewOrdering.Project(null!);
        missing.Should().Throw<ArgumentNullException>();
        var unknown = () => SessionWorkOverviewOrdering.Project(page, (SessionWorkOverviewPageOrder)99);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UnavailableTombstonesStayUnnamedAndTheCompleteSerializedByteCeilingIsEnforced()
    {
        foreach (var availability in Enum.GetValues<SessionWorkOverviewAvailability>().Where(value =>
            value != SessionWorkOverviewAvailability.Observed))
        {
            var tombstone = Row(1, null) with { Availability = availability, Work = null };
            var page = new SessionWorkOverviewPage([tombstone], 1, 0, false, null);
            foreach (var order in Enum.GetValues<SessionWorkOverviewPageOrder>())
            {
                SessionWorkOverviewOrdering.Project(page, order).Single().Should().BeSameAs(tombstone);
                tombstone.Session.Metadata.Should().BeNull();
            }
        }
        var rows = Enumerable.Range(1, 10).Select(index =>
        {
            var row = Row(index, "Saved");
            var request = new HostRequest(new(Guid.NewGuid()), row.Session.Authority.SessionId,
                new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var task = new SessionWorkOverviewTask(request, new(1), new(1), HostTaskState.Unknown, false);
            return row with { Work = row.Work! with { Tasks = [.. Enumerable.Repeat(task, 50)] } };
        }).ToImmutableArray();
        var oversized = new SessionWorkOverviewPage(rows, 10, 0, false, null);
        SessionWorkOverviewPage.GetSerializedSize(oversized).Should().BeGreaterThan(SessionWorkOverviewPage.MaximumResultBytes);
        var project = () => SessionWorkOverviewOrdering.Project(oversized);
        project.Should().Throw<InvalidDataException>();
    }

    private static SessionWorkOverviewRow Row(int id, string? name)
    {
        var session = new HostId<SessionIdentity>(Guid.ParseExact($"00000000-0000-0000-0000-{id:D12}", "D"));
        return new(new(new(session, new(1), true), name is null ? null : new(session, new(1), new(name))),
            SessionWorkOverviewAvailability.Observed,
            new(1, DateTimeOffset.UnixEpoch, 0, 0, 8, 1, 0, [], 0, [], 0, [], 0));
    }
}
