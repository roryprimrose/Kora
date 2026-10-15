using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class LocalEventFilterTests
{
    [Fact]
    public void EveryExactCategoryReasonSourceAndTypeComposeWithoutChangingRows()
    {
        LocalEventCategory?[] categories = [null, .. Enum.GetValues<LocalEventCategory>().Select(value => (LocalEventCategory?)value)];
        LocalEventReason?[] reasons = [null, .. Enum.GetValues<LocalEventReason>().Select(value => (LocalEventReason?)value)];
        LocalEventSource?[] sources = [null, .. Enum.GetValues<LocalEventSource>().Select(value => (LocalEventSource?)value)];
        LocalEventType?[] types = [null, .. Enum.GetValues<LocalEventType>().Select(value => (LocalEventType?)value)];
        var rows = Enum.GetValues<LocalEventType>().SelectMany(type => Enum.GetValues<LocalEventReason>()
            .Select(reason => new LocalEventView(LocalEventTests.Event(type), reason, null))).ToArray();
        foreach (var category in categories)
        {
            foreach (var reason in reasons)
            {
                foreach (var source in sources)
                {
                    foreach (var type in types)
                    {
                        var filter = new LocalEventFilter(category, reason, source, type);
                        filter.IsValid.Should().BeTrue();
                        rows.Where(filter.Matches).Should().Equal(rows.Where(row =>
                            (category is null || row.Event.Category == category) && (reason is null || row.Reason == reason)
                            && (source is null || row.Event.Source == source) && (type is null || row.Event.Type == type)));
                        rows.Where(filter.Matches).Should().Equal(rows.Where(new LocalEventFilter(Category: category).Matches)
                            .Where(new LocalEventFilter(Reason: reason).Matches).Where(new LocalEventFilter(Source: source).Matches)
                            .Where(new LocalEventFilter(Type: type).Matches), "conjunction preserves snapshot order and exact row identities");
                    }
                }
            }
        }
        new LocalEventFilter().Should().Be(new LocalEventFilter(null, null, null, null));
    }

    [Fact]
    public void UnknownFiltersOrUnknownRowEnumsFailClosedRatherThanMatchingAll()
    {
        var row = new LocalEventView(LocalEventTests.Event(), LocalEventReason.Eligible, null);
        foreach (var filter in new[] { new LocalEventFilter((LocalEventCategory)99),
            new LocalEventFilter(Reason: (LocalEventReason)(-1)),
            new LocalEventFilter(Source: (LocalEventSource)99),
            new LocalEventFilter(Source: (LocalEventSource)(-1)),
            new LocalEventFilter(Type: (LocalEventType)99),
            new LocalEventFilter(Type: (LocalEventType)(-1)),
            new LocalEventFilter(Origin: (RequestOrigin)99),
            new LocalEventFilter(Origin: (RequestOrigin)(-1)),
            new LocalEventFilter(QueueReason: new((LocalEventQueueReasonMatch)99)),
            new LocalEventFilter(QueueReason: new((LocalEventQueueReasonMatch)(-1))),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.Any, SessionQueueEligibility.Ready)),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.None, SessionQueueEligibility.Ready)),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.Exact)),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.Exact, (SessionQueueEligibility)99)),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.Exact, (SessionQueueEligibility)(-1))),
            new LocalEventFilter((LocalEventCategory)(-1), (LocalEventReason)99) })
        {
            filter.IsValid.Should().BeFalse();
            filter.Matches(row).Should().BeFalse();
        }
        var all = new LocalEventFilter();
        all.Invoking(filter => filter.Matches(row with { Reason = (LocalEventReason)99 })).Should().Throw<InvalidDataException>();
        all.Invoking(filter => filter.Matches(row with { Event = row.Event with { Type = (LocalEventType)99 } })).Should().Throw<InvalidDataException>();
        all.Invoking(filter => filter.Matches(row with { Event = row.Event with { Source = (LocalEventSource)99 } })).Should().Throw<InvalidDataException>();
    }

    internal static LocalEventQueueReasonFilter[] QueueFilters =>
        [new(), new(LocalEventQueueReasonMatch.None), .. Enum.GetValues<SessionQueueEligibility>()
            .Select(value => new LocalEventQueueReasonFilter(LocalEventQueueReasonMatch.Exact, value))];

    [Fact]
    public void EveryOriginAndQueueReasonMatchesExactlyIncludingNotApplicableWithoutInventingEligibility()
    {
        var rows = Enum.GetValues<SessionQueueEligibility>().SelectMany(queue => new[] { RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice }
            .Select(origin => new LocalEventView(LocalEventTests.Event() with { Origin = origin, QueueReason = queue },
                LocalEventReason.PresentedNoReplay, null)))
            .Concat(new[] { LocalEventType.UserAttention, LocalEventType.MaintenanceAvailable }
                .Select(type => new LocalEventView(LocalEventTests.Event(type), LocalEventReason.CategoryLimit, null))).ToArray();
        RequestOrigin?[] origins = [null, .. Enum.GetValues<RequestOrigin>().Select(value => (RequestOrigin?)value)];
        foreach (var origin in origins)
        {
            foreach (var queue in QueueFilters)
            {
                var filter = new LocalEventFilter(Origin: origin, QueueReason: queue);
                filter.IsValid.Should().BeTrue();
                rows.Where(filter.Matches).Should().Equal(rows.Where(row =>
                    (origin is null || row.Event.Origin == origin)
                    && (queue.Match == LocalEventQueueReasonMatch.Any
                        || (queue.Match == LocalEventQueueReasonMatch.None ? row.Event.QueueReason is null : row.Event.QueueReason == queue.Value))));
            }
        }
        rows.Where(new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.None)).Matches)
            .Should().OnlyContain(row => row.Event.Source != LocalEventSource.LocalVersionQueue
                && row.Reason == LocalEventReason.CategoryLimit && row.Event.QueueReason == null);
        new LocalEventQueueReasonFilter().Should().Be(default(LocalEventQueueReasonFilter));
    }

    [Fact]
    public void AllSixPredicatesComposeAcrossEveryChoicePreservingZeroMatchesAndSnapshotOrder()
    {
        var rows = Enum.GetValues<LocalEventType>().Select(type => new LocalEventView(LocalEventTests.Event(type),
            LocalEventReason.PresentedNoReplay, null)).ToArray();
        LocalEventCategory?[] categories = [null, .. Enum.GetValues<LocalEventCategory>().Select(value => (LocalEventCategory?)value)];
        LocalEventReason?[] reasons = [null, .. Enum.GetValues<LocalEventReason>().Select(value => (LocalEventReason?)value)];
        LocalEventSource?[] sources = [null, .. Enum.GetValues<LocalEventSource>().Select(value => (LocalEventSource?)value)];
        LocalEventType?[] types = [null, .. Enum.GetValues<LocalEventType>().Select(value => (LocalEventType?)value)];
        RequestOrigin?[] origins = [null, .. Enum.GetValues<RequestOrigin>().Select(value => (RequestOrigin?)value)];
        foreach (var category in categories)
        foreach (var reason in reasons)
        foreach (var source in sources)
        foreach (var type in types)
        foreach (var origin in origins)
        foreach (var queue in QueueFilters)
        {
            var filter = new LocalEventFilter(category, reason, source, type, origin, queue);
            var expected = rows.Where(row => (category is null || row.Event.Category == category)
                && (reason is null || row.Reason == reason) && (source is null || row.Event.Source == source)
                && (type is null || row.Event.Type == type) && (origin is null || row.Event.Origin == origin)
                && (queue.Match == LocalEventQueueReasonMatch.Any || (queue.Match == LocalEventQueueReasonMatch.None
                    ? row.Event.QueueReason is null : row.Event.QueueReason == queue.Value)));
            rows.Where(filter.Matches).Should().Equal(expected);
        }
    }

    [Fact]
    public void EntireIrrelevantRowIsValidatedBeforeEvenAnInvalidOrNonmatchingFilterCanExcludeIt()
    {
        var valid = new LocalEventView(LocalEventTests.Event(), LocalEventReason.Deferred, LocalEventTests.Now.AddMinutes(15));
        new LocalEventFilter().Matches(valid).Should().BeTrue();
        var invalidRows = new[]
        {
            valid with { Reason = (LocalEventReason)(-1) },
            valid with { DeferredUntil = LocalEventTests.Now },
            valid with { DeferredUntil = LocalEventTests.Now.AddMinutes(31) },
            valid with { DeferredUntil = LocalEventTests.Now.AddMinutes(15).ToOffset(TimeSpan.FromHours(1)) },
            valid with { Event = valid.Event with { Origin = (RequestOrigin)99 } },
            valid with { Event = valid.Event with { QueueReason = (SessionQueueEligibility)99 } },
            valid with { Event = valid.Event with { Revision = 0 } },
            valid with { Event = valid.Event with { SourceRevision = 0 } },
            valid with { Event = valid.Event with { QueueReason = null } },
            valid with { Event = valid.Event with { QueueProfile = SessionQueueReadProfile.Capability } },
        };
        foreach (var row in invalidRows)
        foreach (var filter in new[] { new LocalEventFilter(), new LocalEventFilter(Origin: RequestOrigin.HostSystem),
            new LocalEventFilter(QueueReason: new(LocalEventQueueReasonMatch.None)), new LocalEventFilter(Type: (LocalEventType)99) })
        {
            filter.Invoking(value => value.Matches(row)).Should().Throw<InvalidDataException>();
        }
        new LocalEventFilter().Matches(valid with { DeferredUntil = valid.Event.ExpiresAt }).Should().BeTrue();
    }

    [Fact]
    public void ImpossibleExactSourceTypePairsAreValidButNeverBroadened()
    {
        var rows = Enum.GetValues<LocalEventType>().Select(type =>
            new LocalEventView(LocalEventTests.Event(type), LocalEventReason.Eligible, null)).ToArray();
        foreach (var source in Enum.GetValues<LocalEventSource>())
        {
            foreach (var type in Enum.GetValues<LocalEventType>())
            {
                var filter = new LocalEventFilter(Source: source, Type: type);
                filter.IsValid.Should().BeTrue();
                var expected = rows.Where(row => row.Event.Source == source && row.Event.Type == type).ToArray();
                rows.Where(filter.Matches).Should().Equal(expected);
                if (expected.Length == 0) { rows.Any(filter.Matches).Should().BeFalse(); }
            }
        }
        Array.Empty<LocalEventView>().Where(new LocalEventFilter().Matches).Should().BeEmpty();
    }
}
