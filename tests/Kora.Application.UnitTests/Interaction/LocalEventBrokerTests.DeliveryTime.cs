using AwesomeAssertions;

using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class LocalEventBrokerTests
{
    [Fact]
    public async Task Completion_delivery_spacing_starts_when_the_authoritative_lease_is_acquired()
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        var deliveredAt = f.Time.Now.AddMinutes(2);
        f.BeforeCurrent = () => f.Time.Now = deliveredAt;

        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.Eligible);
        f.BeforeCurrent = null;
        var next = f.Event(LocalEventType.Completed);
        f.Events = [next with
        {
            Source = LocalEventSource.ConversationTask,
            Id = LocalEvent.Identity(LocalEventSource.ConversationTask, f.Session.Value, next.SubjectId),
            QueueReason = null,
        }];
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.CategoryLimit);
        f.Saved!.Budgets.Single().LastPresentation.Should().Be(deliveredAt);
        f.Saved.HighWatermark.Should().Be(deliveredAt);
        f.Saved.Receipts[0].ChangedAt.Should().Be(deliveredAt);
        f.Saved.Budgets.Single().Count.Should().Be(1);
        f.Time.Now = deliveredAt + LocalEventBrokerState.Spacing - TimeSpan.FromTicks(1);
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.CategoryLimit);
        f.Time.Now = deliveredAt + LocalEventBrokerState.Spacing;
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.Eligible);
        f.Saved.Budgets.Single().Count.Should().Be(2);
        f.Saved.Validate();
    }

    [Fact]
    public async Task Completion_delivery_after_the_fatigue_window_rolls_over_uses_the_new_window()
    {
        using var f = CompletionNoticeActionFixture();
        var before = f.Time.Now;
        f.Saved = new(1, before, [],
            [new(LocalEventCategory.Work, before.AddMinutes(-59), 3, before.AddMinutes(-2))]);
        await using var broker = f.Create();
        f.BeforeCurrent = () => f.Time.Now = before.AddMinutes(2);

        var snapshot = await f.Observe(broker);

        snapshot.Events.Single().Reason.Should().Be(LocalEventReason.Eligible);
        f.Saved.Budgets.Single().Should().Be(new LocalEventCategoryBudget(LocalEventCategory.Work, f.Time.Now, 1, f.Time.Now));
        f.Saved.Receipts.Single().Event.ExpiresAt.Should().Be(f.Events.Single().ExpiresAt);
        f.Saved.Validate();
    }

    [Fact]
    public async Task Clock_rollback_during_observation_lease_wait_cannot_charge_or_deliver_a_completion()
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        var confirmedAt = f.Time.Now;
        var target = (await f.Observe(broker)).Events.Single().Event;
        var saved = LocalEventBrokerState.Serialize(f.Saved!);
        var audits = f.Audits.Count;
        f.BeforeCurrent = () => f.Time.Now = confirmedAt.AddTicks(-1);

        var snapshot = await f.Observe(broker);

        snapshot.Reason.Should().Be(LocalEventReason.ClockRollback);
        snapshot.Events.Should().BeEmpty();
        broker.IsHeld(target).Should().BeFalse();
        LocalEventBrokerState.Serialize(f.Saved!).Should().Be(saved);
        f.Saves.Should().Be(1);
        f.Audits.Should().HaveCount(audits);
        f.Pending.Should().BeFalse();
        broker.IsAvailable.Should().BeTrue();

        f.BeforeCurrent = null;
        f.Time.Now = confirmedAt;
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        f.Saved!.Budgets.Single().Count.Should().Be(1);
    }
}
