using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Interaction;

public sealed class LocalEventBrokerStateTests
{
    [Theory]
    [InlineData(3, 8, 6, 75)]
    [InlineData(11, 1, 5, -45)]
    public void Spring_gap_and_autumn_overlap_cannot_extend_deferral_expiry_or_fatigue(int month, int day, int hour, int localMinutes)
    {
        var transition = new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2026, 1, 1), new(2026, 12, 31),
            TimeSpan.FromHours(1), TimeZoneInfo.TransitionTime.CreateFixedDateRule(transition, 3, 8),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(transition, 11, 1));
        var zone = TimeZoneInfo.CreateCustomTimeZone("deterministic-dst", TimeSpan.FromHours(-5),
            "Test zone", "Standard", "Daylight", [rule]);
        var now = new DateTimeOffset(2026, month, day, hour, 55, 0, TimeSpan.Zero);
        var until = now.Add(LocalEventBrokerState.Deferral);
        var item = LocalEventTests.Event() with { ObservedAt = now, ExpiresAt = now.AddMinutes(30) };
        var receipt = new LocalEventReceipt(item, LocalEventDisposition.Deferred, until, now);
        var state = LocalEventBrokerState.Empty(now) with
        {
            Receipts = [receipt],
            Budgets = [new(LocalEventCategory.Work, now, 1, now)],
        };
        var restored = LocalEventBrokerState.Deserialize(LocalEventBrokerState.Serialize(state));
        (TimeZoneInfo.ConvertTime(until, zone).DateTime - TimeZoneInfo.ConvertTime(now, zone).DateTime)
            .Should().Be(TimeSpan.FromMinutes(localMinutes));
        (until - now).Should().Be(TimeSpan.FromMinutes(15));
        restored.Reason(receipt, until.AddTicks(-1)).Should().Be(LocalEventReason.Deferred);
        restored.Reason(receipt, until).Should().Be(LocalEventReason.Eligible);
        restored.Reason(receipt, item.ExpiresAt).Should().Be(LocalEventReason.Expired);
    }

    private static LocalEventReceipt Receipt(LocalEventDisposition disposition = LocalEventDisposition.Eligible) =>
        new(LocalEventTests.Event(), disposition, null, LocalEventTests.Now);

    [Fact]
    public void UTC_deadlines_deferral_fatigue_restart_and_clock_rollback_are_explicit_not_local_wall_clock()
    {
        var now = LocalEventTests.Now;
        var empty = LocalEventBrokerState.Empty(now);
        var receipt = Receipt();
        empty.Reason(receipt, now).Should().Be(LocalEventReason.Eligible);
        empty.Reason(receipt, now.AddMinutes(-1)).Should().Be(LocalEventReason.ClockRollback);
        empty.Reason(receipt, receipt.Event.ExpiresAt).Should().Be(LocalEventReason.Expired);
        empty.Reason(receipt with { Disposition = LocalEventDisposition.Dismissed }, now).Should().Be(LocalEventReason.Dismissed);
        empty.Reason(receipt with { Disposition = LocalEventDisposition.Presented }, now).Should().Be(LocalEventReason.PresentedNoReplay);
        var deferred = receipt with { Disposition = LocalEventDisposition.Deferred, DeferredUntil = now.AddMinutes(15) };
        empty.Reason(deferred, now).Should().Be(LocalEventReason.Deferred);
        empty.Reason(deferred, now.AddMinutes(15)).Should().Be(LocalEventReason.Eligible);
        var budget = new LocalEventCategoryBudget(LocalEventCategory.Work, now, 1, now);
        var state = empty with { Receipts = [deferred], Budgets = [budget] };
        var restored = LocalEventBrokerState.Deserialize(LocalEventBrokerState.Serialize(state));
        restored.Should().BeEquivalentTo(state);
        restored.Reason(receipt, now.AddSeconds(59)).Should().Be(LocalEventReason.CategoryLimit);
        restored.Reason(receipt, now.AddMinutes(1)).Should().Be(LocalEventReason.Eligible);
        (state with { Budgets = [budget with { Count = 3 }] }).Reason(receipt, now.AddMinutes(1)).Should().Be(LocalEventReason.CategoryLimit);
        var longer = receipt with { Event = receipt.Event with { ExpiresAt = now.AddHours(6) } };
        state.Reason(longer, now.AddHours(1)).Should().Be(LocalEventReason.Eligible);
        foreach (var category in Enum.GetValues<LocalEventCategory>())
        { LocalEventBrokerState.Limit(category).Should().BeInRange(1, 3); }
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("offset")]
    [InlineData("receipts")]
    [InlineData("budgets")]
    [InlineData("receipt-bound")]
    [InlineData("budget-bound")]
    [InlineData("duplicate-event")]
    [InlineData("duplicate-category")]
    [InlineData("disposition")]
    [InlineData("event-future")]
    [InlineData("changed-offset")]
    [InlineData("changed-before")]
    [InlineData("changed-after")]
    [InlineData("deferral-missing")]
    [InlineData("deferral-offset")]
    [InlineData("deferral-past")]
    [InlineData("deferral-deadline")]
    [InlineData("deferral-bound")]
    [InlineData("deferral-unexpected")]
    [InlineData("category")]
    [InlineData("window-offset")]
    [InlineData("window-future")]
    [InlineData("count-zero")]
    [InlineData("count-high")]
    [InlineData("presentation-missing")]
    [InlineData("presentation-offset")]
    [InlineData("presentation-before")]
    [InlineData("presentation-after")]
    public void Unknown_corrupt_and_obsolete_suppression_is_not_defaulted(string field)
    {
        var now = LocalEventTests.Now;
        var receipt = Receipt();
        var budget = new LocalEventCategoryBudget(LocalEventCategory.Work, now, 1, now);
        var state = LocalEventBrokerState.Empty(now) with { Receipts = [receipt], Budgets = [budget] };
        state = field switch
        {
            "schema" => state with { Schema = 2 },
            "offset" => state with { HighWatermark = now.ToOffset(TimeSpan.FromHours(1)) },
            "receipts" => state with { Receipts = null! },
            "budgets" => state with { Budgets = null! },
            "receipt-bound" => state with { Receipts = Enumerable.Repeat(receipt, 65).ToArray() },
            "budget-bound" => state with { Budgets = Enumerable.Repeat(budget, 5).ToArray() },
            "duplicate-event" => state with { Receipts = [receipt, receipt] },
            "duplicate-category" => state with { Budgets = [budget, budget] },
            "disposition" => state with { Receipts = [receipt with { Disposition = (LocalEventDisposition)99 }] },
            "event-future" => state with { Receipts = [receipt with { Event = receipt.Event with { ObservedAt = now.AddSeconds(1) } }] },
            "changed-offset" => state with { Receipts = [receipt with { ChangedAt = now.ToOffset(TimeSpan.FromHours(1)) }] },
            "changed-before" => state with { Receipts = [receipt with { ChangedAt = now.AddSeconds(-1) }] },
            "changed-after" => state with { Receipts = [receipt with { ChangedAt = now.AddSeconds(1) }] },
            "deferral-missing" => state with { Receipts = [receipt with { Disposition = LocalEventDisposition.Deferred }] },
            "deferral-offset" => state with { Receipts = [receipt with { Disposition = LocalEventDisposition.Deferred, DeferredUntil = now.AddMinutes(1).ToOffset(TimeSpan.FromHours(1)) }] },
            "deferral-past" => state with { Receipts = [receipt with { Disposition = LocalEventDisposition.Deferred, DeferredUntil = now }] },
            "deferral-deadline" => state with { Receipts = [receipt with { Disposition = LocalEventDisposition.Deferred, DeferredUntil = now.AddHours(1) }] },
            "deferral-bound" => state with { Receipts = [receipt with { Disposition = LocalEventDisposition.Deferred, DeferredUntil = now.AddMinutes(16) }] },
            "deferral-unexpected" => state with { Receipts = [receipt with { DeferredUntil = now.AddMinutes(1) }] },
            "category" => state with { Budgets = [budget with { Category = (LocalEventCategory)99 }] },
            "window-offset" => state with { Budgets = [budget with { WindowStart = now.ToOffset(TimeSpan.FromHours(1)) }] },
            "window-future" => state with { Budgets = [budget with { WindowStart = now.AddSeconds(1) }] },
            "count-zero" => state with { Budgets = [budget with { Count = 0 }] },
            "count-high" => state with { Budgets = [budget with { Count = 4 }] },
            "presentation-missing" => state with { Budgets = [budget with { LastPresentation = null }] },
            "presentation-offset" => state with { Budgets = [budget with { LastPresentation = now.ToOffset(TimeSpan.FromHours(1)) }] },
            "presentation-before" => state with { Budgets = [budget with { LastPresentation = now.AddSeconds(-1) }] },
            _ => state with { Budgets = [budget with { LastPresentation = now.AddSeconds(1) }] },
        };
        state.Invoking(value => LocalEventBrokerState.Serialize(value)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Schema\":99}")]
    [InlineData("{\"Unknown\":1}")]
    public void Invalid_serialized_state_fails_closed(string text) =>
        ((Action)(() => LocalEventBrokerState.Deserialize(text))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Maximum_valid_content_free_state_is_below_the_complete_storage_byte_bound()
    {
        var receipts = Enumerable.Range(1, LocalEventBrokerState.MaximumReceipts).Select(index =>
        {
            var subject = Guid.ParseExact(index.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), "N");
            var item = LocalEventTests.Event() with
            {
                SubjectId = subject, Id = LocalEvent.Identity(LocalEventSource.LocalVersionQueue, LocalEventTests.Event().SessionId.Value, subject),
                Revision = long.MaxValue, Generation = long.MaxValue, SourceRevision = long.MaxValue, RelatedRevision = long.MaxValue,
            };
            return new LocalEventReceipt(item, LocalEventDisposition.Deferred, LocalEventTests.Now.AddMinutes(15), LocalEventTests.Now);
        }).ToArray();
        var budgets = Enum.GetValues<LocalEventCategory>().Select(category =>
            new LocalEventCategoryBudget(category, LocalEventTests.Now, LocalEventBrokerState.Limit(category), LocalEventTests.Now)).ToArray();
        var state = LocalEventBrokerState.Empty(LocalEventTests.Now) with { Receipts = receipts, Budgets = budgets };
        var serialized = LocalEventBrokerState.Serialize(state);
        System.Text.Encoding.UTF8.GetByteCount(serialized).Should().BeLessThan(LocalEventBrokerState.MaximumBytes);
        LocalEventBrokerState.Deserialize(serialized).Should().BeEquivalentTo(state);
    }

    [Fact]
    public void Noncanonical_duplicate_oversized_and_null_record_envelopes_are_rejected_without_content_recovery()
    {
        var serialized = LocalEventBrokerState.Serialize(LocalEventBrokerState.Empty(LocalEventTests.Now));
        ((Action)(() => LocalEventBrokerState.Deserialize(" " + serialized))).Should().Throw<InvalidDataException>();
        ((Action)(() => LocalEventBrokerState.Deserialize(serialized.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1", StringComparison.Ordinal)))).Should().Throw<InvalidDataException>();
        ((Action)(() => LocalEventBrokerState.Deserialize(new string(' ', LocalEventBrokerState.MaximumBytes + 1)))).Should().Throw<InvalidDataException>();
        ((Action)(() => LocalEventBrokerState.Deserialize(serialized.Replace("\"Receipts\":[]", "\"Receipts\":[null]", StringComparison.Ordinal)))).Should().Throw<InvalidDataException>();
        var receipt = Receipt();
        var state = LocalEventBrokerState.Empty(LocalEventTests.Now) with { Receipts = [receipt] };
        LocalEventBrokerState.Serialize(state).Should().NotContain(receipt.Event.Summary);
    }
}
