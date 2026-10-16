using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed partial class LocalEventFilterTests
{
    private static LocalEventFilter Range(int field, string value, LocalEventFilter? filter = null) => field switch
    {
        0 => (filter ?? new()) with { MinimumRevision = value },
        1 => (filter ?? new()) with { MaximumRevision = value },
        2 => (filter ?? new()) with { MinimumGeneration = value },
        3 => (filter ?? new()) with { MaximumGeneration = value },
        4 => (filter ?? new()) with { ObservedFromUtc = value },
        5 => (filter ?? new()) with { ObservedUntilUtc = value },
        6 => (filter ?? new()) with { ExpiresFromUtc = value },
        _ => (filter ?? new()) with { ExpiresUntilUtc = value },
    };

    [Fact]
    public void RangeAdmissionRejectsMalformedNonpositiveOverflowNoncanonicalNullAndReversedBounds()
    {
        var row = new LocalEventView(LocalEventTests.Event(), LocalEventReason.Eligible, null);
        for (var field = 0; field < 8; field++)
        {
            string?[] invalid = field < 4
                ? [null, " ", "0", "-1", "+1", "1.0", "1,000", " 1", "1 ", "1\n", "١", "9223372036854775808"]
                : [null, " ", "2026-10-04", "2026-10-04T01:55:00Z", "2026-10-04T01:55:00.0000000Z",
                    "2026-10-04T01:55:00.0000000+01:00", "2026-10-04T01:55:00+00:00",
                    "2026-10-04T01:55:00.0000000-00:00", "2026-10-04T01:55:00.0000000+00:00 ",
                    "2026-02-30T01:55:00.0000000+00:00"];
            foreach (var value in invalid)
            {
                var filter = Range(field, value!);
                filter.IsValid.Should().BeFalse();
                filter.Matches(row).Should().BeFalse();
                filter.Invoking(f => f.Matches(row with { Reason = (LocalEventReason)99 }))
                    .Should().Throw<InvalidDataException>("invalid candidates cannot be hidden by an invalid filter");
            }
        }
        for (var field = 0; field < 8; field += 2)
        {
            var filter = Range(field + 1, field < 4 ? "1" : Utc(LocalEventTests.Now),
                Range(field, field < 4 ? "2" : Utc(LocalEventTests.Now.AddTicks(1))));
            filter.IsValid.Should().BeFalse();
            filter.Matches(row).Should().BeFalse();
        }
    }

    [Fact]
    public void InclusiveIndependentOpenAndEqualBoundsPreserveSnapshotOrderAndAndWithEveryMetadataPredicate()
    {
        var rows = Enumerable.Range(1, 3).Select(index =>
            new LocalEventView(LocalEventTests.Event() with
            {
                Revision = index, Generation = index,
                ObservedAt = LocalEventTests.Now.AddMinutes(index),
                ExpiresAt = LocalEventTests.Now.AddMinutes(30 + index),
            }, LocalEventReason.Eligible, null)).Reverse().ToArray();
        foreach (var target in rows)
        {
            var item = target.Event;
            var all = new LocalEventFilter(item.Category, target.Reason, item.Source, item.Type, item.Origin,
                new(LocalEventQueueReasonMatch.Exact, item.QueueReason),
                item.SessionId.Value.ToString("D"), item.TaskId!.Value.Value.ToString("D"), item.SubjectId.ToString("D"));
            string[] exact = [item.Revision.ToString(CultureInfo.InvariantCulture), item.Revision.ToString(CultureInfo.InvariantCulture),
                item.Generation.ToString(CultureInfo.InvariantCulture), item.Generation.ToString(CultureInfo.InvariantCulture),
                Utc(item.ObservedAt), Utc(item.ObservedAt), Utc(item.ExpiresAt), Utc(item.ExpiresAt)];
            for (var field = 0; field < 8; field++)
            {
                all = Range(field, exact[field], all);
                all.IsValid.Should().BeTrue();
                all.Matches(target).Should().BeTrue("equality is inclusive for every bound");
                var excluded = field < 4
                    ? (field % 2 == 0 ? 4 : 1).ToString(CultureInfo.InvariantCulture)
                    : Utc((field < 6 ? item.ObservedAt : item.ExpiresAt).AddTicks(field % 2 == 0 ? 1 : -1));
                if (field % 2 == 0)
                {
                    Range(field, excluded).Matches(target).Should().BeFalse();
                }
                else
                {
                    Range(field, field < 4 ? "1" : excluded).Matches(target).Should().Be(field < 4 && item.Revision == 1);
                }
                rows.Where(Range(field, exact[field]).Matches).Should().Equal(rows.Where(row => field switch
                {
                    0 => row.Event.Revision >= item.Revision,
                    1 => row.Event.Revision <= item.Revision,
                    2 => row.Event.Generation >= item.Generation,
                    3 => row.Event.Generation <= item.Generation,
                    4 => row.Event.ObservedAt >= item.ObservedAt,
                    5 => row.Event.ObservedAt <= item.ObservedAt,
                    6 => row.Event.ExpiresAt >= item.ExpiresAt,
                    _ => row.Event.ExpiresAt <= item.ExpiresAt,
                }));
            }
            rows.Where(all.Matches).Should().Equal([target]);
            foreach (var mismatch in new[]
            {
                all with { Category = LocalEventCategory.Maintenance },
                all with { Reason = LocalEventReason.Dismissed },
                all with { Source = LocalEventSource.CachedMaintenance },
                all with { Type = LocalEventType.Failed },
                all with { Origin = Kora.Core.Hosting.RequestOrigin.HostSystem },
                all with { QueueReason = new(LocalEventQueueReasonMatch.None) },
                all with { SessionId = Guid.NewGuid().ToString("D") },
                all with { TaskId = Guid.NewGuid().ToString("D") },
                all with { SubjectId = Guid.NewGuid().ToString("D") },
            })
            { mismatch.Matches(target).Should().BeFalse(); }
        }
        new LocalEventFilter(MinimumRevision: "0001", MaximumGeneration: long.MaxValue.ToString(CultureInfo.InvariantCulture))
            .Matches(rows[0]).Should().BeTrue();
        new LocalEventFilter(ObservedFromUtc: Utc(DateTimeOffset.MinValue), ExpiresUntilUtc: Utc(DateTimeOffset.MaxValue))
            .Matches(rows[0]).Should().BeTrue();
    }

    [Fact]
    public void CompleteCandidatesAreValidatedEvenWhenEveryRangeWouldExcludeThem()
    {
        var valid = new LocalEventView(LocalEventTests.Event(), LocalEventReason.Eligible, null);
        var invalid = new[]
        {
            valid with { Event = valid.Event with { Revision = 0 } },
            valid with { Event = valid.Event with { Generation = 0 } },
            valid with { Event = valid.Event with { ObservedAt = valid.Event.ObservedAt.ToOffset(TimeSpan.FromHours(1)) } },
            valid with { Event = valid.Event with { ExpiresAt = valid.Event.ObservedAt } },
            valid with { Reason = (LocalEventReason)99 },
            valid with { DeferredUntil = valid.Event.ExpiresAt.AddTicks(1) },
        };
        for (var field = 0; field < 8; field++)
        foreach (var row in invalid)
        {
            var filter = Range(field, field < 4 ? "999" : Utc(LocalEventTests.Now.AddDays(1)));
            filter.Invoking(f => f.Matches(row)).Should().Throw<InvalidDataException>();
        }
    }

    private static string Utc(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
