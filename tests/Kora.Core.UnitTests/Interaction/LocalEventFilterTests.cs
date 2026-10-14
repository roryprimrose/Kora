using AwesomeAssertions;

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
            new LocalEventFilter((LocalEventCategory)(-1), (LocalEventReason)99) })
        {
            filter.IsValid.Should().BeFalse();
            filter.Matches(row).Should().BeFalse();
        }
        var all = new LocalEventFilter();
        all.Matches(row with { Reason = (LocalEventReason)99 }).Should().BeFalse();
        all.Matches(row with { Event = row.Event with { Type = (LocalEventType)99 } }).Should().BeFalse();
        all.Matches(row with { Event = row.Event with { Source = (LocalEventSource)99 } }).Should().BeFalse();
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
