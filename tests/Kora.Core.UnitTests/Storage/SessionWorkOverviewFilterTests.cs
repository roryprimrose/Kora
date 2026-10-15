using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewFilterTests
{
    [Fact]
    public void AvailabilityIsExactAndContentFreeForEveryDefinedValueAndAny()
    {
        var id = new HostId<SessionIdentity>(Guid.NewGuid());
        var entry = new SessionWorkspaceEntry(new(id, new(1), true), null);
        foreach (var actual in Enum.GetValues<SessionWorkOverviewAvailability>())
        {
            var row = new SessionWorkOverviewRow(entry, actual, null);
            SessionWorkOverviewFilter.Matches(null, row).Should().BeTrue();
            foreach (var filter in Enum.GetValues<SessionWorkOverviewAvailability>())
            {
                SessionWorkOverviewFilter.Matches(filter, row).Should().Be(filter == actual);
            }
        }
        var invalidFilter = () => SessionWorkOverviewFilter.Validate((SessionWorkOverviewAvailability)99);
        invalidFilter.Should().Throw<ArgumentOutOfRangeException>();
        var invalidMatch = () => SessionWorkOverviewFilter.Matches((SessionWorkOverviewAvailability)99,
            new(entry, SessionWorkOverviewAvailability.Observed, null));
        invalidMatch.Should().Throw<ArgumentOutOfRangeException>();
        foreach (var filter in new SessionWorkOverviewAvailability?[] { null, SessionWorkOverviewAvailability.Observed })
        {
            var invalidRow = () => SessionWorkOverviewFilter.Matches(filter, new(entry, (SessionWorkOverviewAvailability)99, null));
            invalidRow.Should().Throw<InvalidDataException>();
        }
    }
}
