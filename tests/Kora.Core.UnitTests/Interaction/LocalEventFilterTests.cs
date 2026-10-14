using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class LocalEventFilterTests
{
    [Fact]
    public void EveryExactCategoryAndReasonComposeOverEveryAuthoritativeTypeWithoutChangingRows()
    {
        LocalEventCategory?[] categories = [null, .. Enum.GetValues<LocalEventCategory>().Select(value => (LocalEventCategory?)value)];
        LocalEventReason?[] reasons = [null, .. Enum.GetValues<LocalEventReason>().Select(value => (LocalEventReason?)value)];
        var rows = Enum.GetValues<LocalEventType>().SelectMany(type => Enum.GetValues<LocalEventReason>()
            .Select(reason => new LocalEventView(LocalEventTests.Event(type), reason, null))).ToArray();
        foreach (var category in categories)
        {
            foreach (var reason in reasons)
            {
                var filter = new LocalEventFilter(category, reason);
                filter.IsValid.Should().BeTrue();
                rows.Where(filter.Matches).Should().Equal(rows.Where(row =>
                    (category is null || row.Event.Category == category) && (reason is null || row.Reason == reason)));
                rows.Where(filter.Matches).Should().Equal(rows.Where(new LocalEventFilter(null, reason).Matches)
                    .Where(new LocalEventFilter(category).Matches), "composition preserves snapshot order and exact row identities");
            }
        }
        new LocalEventFilter().Should().Be(new LocalEventFilter(null, null));
    }

    [Fact]
    public void UnknownFiltersOrUnknownRowEnumsFailClosedRatherThanMatchingAll()
    {
        var row = new LocalEventView(LocalEventTests.Event(), LocalEventReason.Eligible, null);
        foreach (var filter in new[] { new LocalEventFilter((LocalEventCategory)99),
            new LocalEventFilter(Reason: (LocalEventReason)(-1)),
            new LocalEventFilter((LocalEventCategory)(-1), (LocalEventReason)99) })
        {
            filter.IsValid.Should().BeFalse();
            filter.Matches(row).Should().BeFalse();
        }
        var all = new LocalEventFilter();
        all.Matches(row with { Reason = (LocalEventReason)99 }).Should().BeFalse();
        all.Matches(row with { Event = row.Event with { Type = (LocalEventType)99 } }).Should().BeFalse();
    }
}
