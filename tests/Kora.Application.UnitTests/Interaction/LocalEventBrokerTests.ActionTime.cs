using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class LocalEventBrokerTests
{
    [Theory]
    [InlineData(LocalEventOperation.Status, "read-expiry")]
    [InlineData(LocalEventOperation.Review, "read-expiry")]
    [InlineData(LocalEventOperation.Dismiss, "read-expiry")]
    [InlineData(LocalEventOperation.Defer, "read-expiry")]
    [InlineData(LocalEventOperation.Status, "lease-expiry")]
    [InlineData(LocalEventOperation.Review, "lease-expiry")]
    [InlineData(LocalEventOperation.Dismiss, "lease-expiry")]
    [InlineData(LocalEventOperation.Defer, "lease-expiry")]
    [InlineData(LocalEventOperation.Status, "read-rollback")]
    [InlineData(LocalEventOperation.Review, "read-rollback")]
    [InlineData(LocalEventOperation.Dismiss, "read-rollback")]
    [InlineData(LocalEventOperation.Defer, "read-rollback")]
    [InlineData(LocalEventOperation.Status, "lease-rollback")]
    [InlineData(LocalEventOperation.Review, "lease-rollback")]
    [InlineData(LocalEventOperation.Dismiss, "lease-rollback")]
    [InlineData(LocalEventOperation.Defer, "lease-rollback")]
    public async Task Exact_actions_recheck_time_under_source_lease_without_acknowledging_or_mutating_retired_notices(
        LocalEventOperation operation, string boundary)
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        var saved = LocalEventBrokerState.Serialize(f.Saved!);
        var audits = f.Audits.Count;
        var rollback = boundary.EndsWith("rollback", StringComparison.Ordinal);
        void ChangeTime() => f.Time.Now = rollback ? f.Saved!.HighWatermark.AddTicks(-1) : target.ExpiresAt;
        if (boundary.StartsWith("read", StringComparison.Ordinal))
        {
            f.BeforeRead = _ => { ChangeTime(); return Task.CompletedTask; };
        }
        else { f.BeforeCurrent = ChangeTime; }

        var result = await broker.ExecuteAsync(new(operation, target.Id, target.Revision),
            RequestOrigin.ActivatedVoice, () => true, f.Token);

        result.Should().Be(new LocalEventView(target,
            rollback ? LocalEventReason.ClockRollback : LocalEventReason.Expired, null));
        LocalEventBrokerState.Serialize(f.Saved!).Should().Be(saved);
        f.Saves.Should().Be(1);
        f.Audits.Should().HaveCount(audits);
        f.Pending.Should().BeFalse();
        broker.IsAvailable.Should().BeTrue();

        f.BeforeRead = null;
        f.BeforeCurrent = null;
        f.Time.Now = rollback ? target.ObservedAt : target.ExpiresAt;
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(
            rollback ? LocalEventReason.PresentedNoReplay : LocalEventReason.Expired);
    }

    [Theory]
    [InlineData(LocalEventOperation.Dismiss, 2)]
    [InlineData(LocalEventOperation.Dismiss, 20)]
    [InlineData(LocalEventOperation.Defer, 2)]
    [InlineData(LocalEventOperation.Defer, 20)]
    public async Task Exact_suppression_uses_commit_time_after_source_wait_and_never_renews_original_expiry(
        LocalEventOperation operation, int delayMinutes)
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        f.BeforeCurrent = () => f.Time.Now = target.ObservedAt.AddMinutes(delayMinutes);

        var result = await broker.ExecuteAsync(new(operation, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);

        result.Reason.Should().Be(operation == LocalEventOperation.Defer ? LocalEventReason.Deferred : LocalEventReason.Dismissed);
        result.Event.ExpiresAt.Should().Be(target.ExpiresAt);
        result.Event.Revision.Should().Be(target.Revision + 1);
        f.Saved!.HighWatermark.Should().Be(f.Time.Now);
        f.Saved.Receipts.Single().ChangedAt.Should().Be(f.Time.Now);
        f.Saved.Budgets.Single().LastPresentation.Should().Be(target.ObservedAt);
        if (operation == LocalEventOperation.Defer)
        {
            result.DeferredUntil.Should().Be(delayMinutes == 2 ? f.Time.Now.AddMinutes(15) : target.ExpiresAt);
        }
        else { result.DeferredUntil.Should().BeNull(); }
        f.Saved.Validate();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("rollback")]
    [InlineData("admission")]
    public async Task Time_or_admission_revoked_during_requested_audit_cannot_persist_or_confirm_dismissal(string boundary)
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        var saved = LocalEventBrokerState.Serialize(f.Saved!);
        f.OnAudit = entry =>
        {
            if (string.Equals(entry.ActionId, "local-event.dismiss", StringComparison.Ordinal)
                && entry.Outcome == SecurityAuditOutcome.Requested)
            {
                if (boundary is "admission") { f.ControlRevision++; }
                else { f.Time.Now = boundary is "rollback" ? f.Saved!.HighWatermark.AddTicks(-1) : target.ExpiresAt; }
            }
        };
        var dismiss = () => broker.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);

        await dismiss.Should().ThrowAsync<InvalidOperationException>();
        LocalEventBrokerState.Serialize(f.Saved!).Should().Be(saved);
        f.Saves.Should().Be(1);
        f.Pending.Should().BeFalse();
        f.Audits.Where(item => string.Equals(item.Event.ActionId, "local-event.dismiss", StringComparison.Ordinal))
            .Select(item => item.Event.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        broker.IsAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("replacement")]
    [InlineData("session")]
    public async Task Awaited_dismissal_cannot_outlive_cancellation_source_replacement_or_selected_session(string boundary)
    {
        using var f = CompletionNoticeActionFixture();
        await using var broker = f.Create();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var current = true;
        var target = (await broker.ObserveAsync(f.Session, () => current, f.Token)).Events.Single().Event;
        var saved = LocalEventBrokerState.Serialize(f.Saved!);
        var audits = f.Audits.Count;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var pending = broker.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, cancellation.Token);
        await entered.Task.WaitAsync(f.Token);
        if (boundary is "cancel") { await cancellation.CancelAsync(); }
        else if (boundary is "replacement") { f.Events = []; }
        else { current = false; }
        release.TrySetResult();

        var dismiss = () => pending;
        if (boundary is "cancel") { await dismiss.Should().ThrowAsync<OperationCanceledException>(); }
        else if (boundary is "replacement")
        { (await pending).Reason.Should().Be(LocalEventReason.SourceUnavailable); }
        else { await dismiss.Should().ThrowAsync<InvalidOperationException>(); }

        LocalEventBrokerState.Serialize(f.Saved!).Should().Be(saved);
        f.Saves.Should().Be(1);
        f.Audits.Should().HaveCount(audits);
        f.Pending.Should().BeFalse();
        broker.IsAvailable.Should().BeTrue();
        f.BeforeRead = null;
        current = true;
        var refreshed = await f.Observe(broker);
        if (boundary is "replacement") { refreshed.Events.Should().BeEmpty(); }
        else { refreshed.Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay); }
    }

    private static Fixture CompletionNoticeActionFixture()
    {
        var fixture = new Fixture();
        var item = fixture.Event(LocalEventType.Completed) with { Source = LocalEventSource.ConversationTask, QueueReason = null };
        fixture.Events = [item with { Id = LocalEvent.Identity(item.Source, item.SessionId.Value, item.SubjectId) }];
        return fixture;
    }
}
