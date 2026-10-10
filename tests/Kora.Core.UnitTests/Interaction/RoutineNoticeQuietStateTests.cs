using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class RoutineNoticeQuietStateTests
{
    [Theory]
    [InlineData(LocalEventCategory.Work, true)]
    [InlineData(LocalEventCategory.Maintenance, true)]
    [InlineData(LocalEventCategory.Failure, false)]
    [InlineData(LocalEventCategory.Attention, false)]
    [InlineData((LocalEventCategory)99, false)]
    public void IncludesOnlyRoutineCategories(LocalEventCategory category, bool included) =>
        RoutineNoticeQuietState.Includes(category).Should().Be(included);

    [Fact]
    public void KnownVersionsPreserveCanonicalHistoryButNeverSerializeQuietChoice()
    {
        var now = LocalEventTests.Now;
        var receipt = new LocalEventReceipt(LocalEventTests.Event(), LocalEventDisposition.RoutineSuppressed, null, now);
        var legacy = LocalEventBrokerState.Empty(now);
        var legacyBytes = LocalEventBrokerState.Serialize(legacy);
        LocalEventBrokerState.Serialize(LocalEventBrokerState.Deserialize(legacyBytes)).Should().Be(legacyBytes);
        var current = legacy with { Schema = 2, Receipts = [receipt] };
        var bytes = LocalEventBrokerState.Serialize(current);
        LocalEventBrokerState.Deserialize(bytes).Should().BeEquivalentTo(current);
        bytes.Should().NotContain("Enabled").And.NotContain("RoutineQuiet");
        current.Reason(receipt, now).Should().Be(LocalEventReason.RoutineSuppressedNoReplay);
        current.Reason(receipt, now.AddTicks(-1)).Should().Be(LocalEventReason.ClockRollback);
        current.Reason(receipt, receipt.Event.ExpiresAt).Should().Be(LocalEventReason.Expired);
        new LocalEventView(receipt.Event, current.Reason(receipt, now), null).DisplaySummary.Should().Contain("No new notification");
        var old = () => (current with { Schema = 1 }).Validate();
        old.Should().Throw<InvalidDataException>();
        foreach (var type in new[] { LocalEventType.Failed, LocalEventType.UserAttention })
        {
            var required = () => (current with
            { Receipts = [receipt with { Event = receipt.Event with { Type = type } }] }).Validate();
            required.Should().Throw<InvalidDataException>();
        }
    }
}
