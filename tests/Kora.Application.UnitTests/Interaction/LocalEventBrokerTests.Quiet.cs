using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class LocalEventBrokerTests
{
    private static async Task<RoutineNoticeQuietState> ChangeQuiet(Fixture f, LocalEventBroker broker,
        bool enabled, Func<bool>? native = null, long? revision = null, CancellationToken? token = null)
    {
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Session,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        return await broker.ChangeRoutineQuietNativeAsync(f.Session, enabled, revision ?? broker.RoutineQuiet.Revision,
            native ?? (() => true), token ?? f.Token);
    }

    [Fact]
    public async Task QuietBeforeFirstObservationSuppressesRoutineBeforePresentationButNeverRequiredCategories()
    {
        using var f = new Fixture();
        f.Events = [f.Event(), f.Event(LocalEventType.MaintenanceAvailable),
            f.Event(LocalEventType.Failed), f.Event(LocalEventType.UserAttention)];
        await using var broker = f.Create();
        broker.RoutineQuiet.Should().Be(new RoutineNoticeQuietState(false, 0));
        await ChangeQuiet(f, broker, true);
        f.Saved!.Budgets.Should().BeEmpty();
        var snapshot = await f.Observe(broker);
        snapshot.Events.Should().HaveCount(2).And.OnlyContain(item =>
            item.Reason == LocalEventReason.Eligible && !RoutineNoticeQuietState.Includes(item.Event.Category));
        snapshot.Omitted.Should().Be(2);
        snapshot.RoutineOmitted.Should().Be(2);
        snapshot.RoutineSuppressed.Should().Be(2);
        snapshot.RoutineQuiet.Enabled.Should().BeTrue();
        f.Saved.Receipts.Count(item => item.Disposition == LocalEventDisposition.RoutineSuppressed).Should().Be(2);
        f.Saved.Budgets.Should().OnlyContain(item =>
            item.Category == LocalEventCategory.Failure || item.Category == LocalEventCategory.Attention);
        f.Audits.Should().Contain(item => item.Event.ActionId == "local-event.routine-quiet.on"
            && item.Event.Outcome == SecurityAuditOutcome.Succeeded && item.Session == f.Session.Value);
        var quietAudit = f.Audits.Where(item => item.Event.ActionId is "local-event.routine-quiet.on").ToArray();
        quietAudit.Select(item => item.Event.CorrelationId).Distinct().Should().ContainSingle();
        f.Audits.Where(item => item.Event.ActionId is "local-event.quiet-suppression")
            .Should().OnlyContain(item => item.Event.CorrelationId != quietAudit[0].Event.CorrelationId);
        f.Pending.Should().BeFalse();
        broker.IsCurrent(snapshot).Should().BeTrue();
    }

    [Fact]
    public async Task ClearBurnsUnobservedMutedSourceAndDeferralWithoutResettingBudgetsOrReplayingAfterRestart()
    {
        using var f = new Fixture();
        var broker = f.Create();
        var target = (await f.Observe(broker)).Events.Single().Event;
        await broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        var budgets = f.Saved!.Budgets;
        await ChangeQuiet(f, broker, true);
        var unseen = f.Event(LocalEventType.MaintenanceAvailable);
        f.Events = [f.Events[0], unseen];
        await ChangeQuiet(f, broker, false);
        f.Saved!.Budgets.Should().BeEquivalentTo(budgets);
        f.Saved.Receipts.Should().OnlyContain(item => item.Disposition == LocalEventDisposition.RoutineSuppressed && item.DeferredUntil == null);
        var clear = await f.Observe(broker);
        clear.Events.Should().OnlyContain(item => item.Reason == LocalEventReason.RoutineSuppressedNoReplay);
        clear.RoutineOmitted.Should().Be(0);
        clear.RoutineSuppressed.Should().Be(2);
        var suppressed = clear.Events[0].Event;
        var defer = () => broker.ExecuteAsync(new(LocalEventOperation.Defer, suppressed.Id, suppressed.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        await defer.Should().ThrowAsync<InvalidOperationException>().WithMessage("*backlog*");
        await ChangeQuiet(f, broker, false); // Explicit default reset is not a budget/history reset.
        await broker.DisposeAsync();
        broker.IsCurrent(clear).Should().BeFalse();
        await using var restarted = f.Create();
        restarted.RoutineQuiet.Should().Be(new RoutineNoticeQuietState(false, 0));
        (await f.Observe(restarted)).Events.Should().OnlyContain(item => item.Reason == LocalEventReason.RoutineSuppressedNoReplay);
        f.Time.Now = f.Time.Now.AddMinutes(1);
        f.Events = [.. f.Events, f.Event()];
        (await f.Observe(restarted)).Events.Should().ContainSingle(item => item.Reason == LocalEventReason.Eligible);
        f.Saved.Budgets.Single(item => item.Category == LocalEventCategory.Work).Count.Should().Be(2);
        f.Time.Now = target.ExpiresAt;
        (await f.Observe(restarted)).Events.Where(item => item.Event.Id == target.Id)
            .Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.Expired);
    }

    [Fact]
    public async Task QuietAfterPresentationPreservesTruthAndRejectsOldRenderAndExactActionCallbacks()
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var old = await f.Observe(broker);
        await ChangeQuiet(f, broker, true);
        broker.IsCurrent(old).Should().BeFalse();
        f.Saved!.Receipts.Single().Disposition.Should().Be(LocalEventDisposition.Presented);
        (await f.Observe(broker)).Events.Should().BeEmpty();
        var target = old.Events.Single().Event;
        var defer = () => broker.ExecuteAsync(new(LocalEventOperation.Defer, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        await defer.Should().ThrowAsync<InvalidOperationException>();
        await ChangeQuiet(f, broker, false);
        var passive = await f.Observe(broker);
        passive.Events.Single().Reason.Should().Be(LocalEventReason.PresentedNoReplay);
        f.Events = [target with { SourceRevision = 2 }];
        await ChangeQuiet(f, broker, true);
        f.Saved.Receipts.Single().Disposition.Should().Be(LocalEventDisposition.RoutineSuppressed);
        f.Saved.Receipts.Single().Event.Revision.Should().Be(2);
        await ChangeQuiet(f, broker, false);
        (await f.Observe(broker)).Events.Single().Reason.Should().Be(LocalEventReason.RoutineSuppressedNoReplay);
        target = (await f.Observe(broker)).Events.Single().Event;
        var dismissed = await broker.ExecuteAsync(new(LocalEventOperation.Dismiss, target.Id, target.Revision),
            RequestOrigin.LocalUi, () => true, f.Token);
        dismissed.Reason.Should().Be(LocalEventReason.Dismissed);
        await ChangeQuiet(f, broker, true);
        f.Saved.Receipts.Single().Disposition.Should().Be(LocalEventDisposition.Dismissed);
    }

    [Fact]
    public async Task FullVisibleAndReceiptBoundsCountHiddenRoutineWithoutEvictionOrConsumption()
    {
        using var f = new Fixture();
        f.Events = [.. Enumerable.Range(0, 8).Select(_ => f.Event(LocalEventType.Failed)),
            .. Enumerable.Range(0, 8).Select(_ => f.Event(LocalEventType.UserAttention)),
            .. Enumerable.Range(0, 48).Select(_ => f.Event())];
        await using var broker = f.Create();
        await ChangeQuiet(f, broker, true);
        var snapshot = await f.Observe(broker);
        snapshot.Events.Should().HaveCount(8);
        snapshot.Omitted.Should().Be(56);
        snapshot.RoutineOmitted.Should().Be(48);
        snapshot.RoutineSuppressed.Should().Be(48);
        f.Saved!.Receipts.Should().HaveCount(64);
        f.Events = [.. f.Events, f.Event(LocalEventType.MaintenanceAvailable)];
        var capacity = () => ChangeQuiet(f, broker, false);
        await capacity.Should().ThrowAsync<InvalidOperationException>().WithMessage("*capacity*");
        f.Saved.Receipts.Should().HaveCount(64);
        broker.RoutineQuiet.Enabled.Should().BeTrue();
        broker.IsCurrent(snapshot).Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task QuietChoiceCannotBeRelabelledFromModelSystemOrVoiceOrigins(RequestOrigin origin)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.Session,
            new(Guid.NewGuid()), origin), HostActivityLayer.Application, HostOperation.Request);
        var change = () => broker.ChangeRoutineQuietNativeAsync(f.Session, true, 0, () => true, f.Token);
        await change.Should().ThrowAsync<InvalidOperationException>();
        f.Saves.Should().Be(0);
        f.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("completed")]
    [InlineData("native")]
    [InlineData("private")]
    [InlineData("stale-choice")]
    [InlineData("no-context")]
    public async Task NativeChoiceRequiresLiveExactContextLifetimePrivateControlAndChoiceRevision(string boundary)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var request = new HostRequest(new(Guid.NewGuid()), boundary is "session" ? new(Guid.NewGuid()) : f.Session,
            new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = boundary is "no-context" ? null
            : HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
        if (boundary is "completed") { root!.Dispose(); }
        if (boundary is "private") { f.CanControl = false; }
        var change = () => broker.ChangeRoutineQuietNativeAsync(f.Session, true, boundary is "stale-choice" ? 1 : 0,
            () => boundary is not "native", f.Token);
        await change.Should().ThrowAsync<InvalidOperationException>();
        f.Saves.Should().Be(0);
        f.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("audit-request")]
    [InlineData("quiet-terminal")]
    [InlineData("confirm")]
    [InlineData("source")]
    [InlineData("call")]
    [InlineData("cancel")]
    [InlineData("clock")]
    [InlineData("read-clock")]
    [InlineData("after-source")]
    [InlineData("retired-context")]
    public async Task QuietFailureNeverConfirmsDefaultOrSilentlyReopensBroker(string boundary)
    {
        using var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        await using var broker = f.Create();
        var initial = await f.Observe(broker);
        f.OnSave = () =>
        {
            if (boundary is "save") { throw new IOException("Storage unavailable."); }
            if (boundary is "call") { f.ControlRevision++; }
            if (boundary is "cancel") { cancel.Cancel(); }
        };
        f.OnAudit = entry =>
        {
            if (entry.ActionId.StartsWith("local-event.routine-quiet", StringComparison.Ordinal)
                && (boundary is "audit-request" && entry.Outcome == SecurityAuditOutcome.Requested
                    || boundary is "quiet-terminal" && entry.Outcome == SecurityAuditOutcome.Succeeded))
            { throw new IOException("Trusted audit unavailable."); }
            if (boundary is "retired-context" && entry.Outcome == SecurityAuditOutcome.Succeeded)
            { HostActivity.RequireCurrent().Dispose(); }
        };
        if (boundary is "confirm") { f.OnConfirm = () => throw new IOException("Marker unavailable."); }
        if (boundary is "source") { f.BeforeCurrent = () => f.Events = []; }
        if (boundary is "clock") { f.Time.Now = f.Time.Now.AddTicks(-1); }
        if (boundary is "read-clock") { f.BeforeRead = _ => { f.Time.Now = f.Time.Now.AddTicks(-1); return Task.CompletedTask; }; }
        if (boundary is "after-source") { f.AfterCurrent = () => f.ControlRevision++; }
        var change = () => ChangeQuiet(f, broker, true, token: cancel.Token);
        await change.Should().ThrowAsync<Exception>();
        broker.IsCurrent(initial).Should().BeFalse();
        await f.Reading(broker).Should().ThrowAsync<InvalidOperationException>();
        f.Saved!.Budgets.Single().Count.Should().Be(1);
    }

    [Fact]
    public async Task SerializedObserveToggleAndPrivateReopeningDoNotPublishOldRoutineResultOrReplay()
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var observation = f.Observe(broker);
        await entered.Task;
        var change = ChangeQuiet(f, broker, true);
        release.SetResult();
        var cancelledObservation = () => observation;
        await cancelledObservation.Should().ThrowAsync<InvalidOperationException>();
        await change;
        f.Saved!.Budgets.Should().BeEmpty();
        f.BeforeRead = null;
        f.CanControl = false;
        await f.Reading(broker).Should().ThrowAsync<InvalidOperationException>();
        f.CanControl = true;
        f.ControlRevision++;
        var quiet = await f.Observe(broker);
        quiet.Events.Should().BeEmpty();
        quiet.RoutineOmitted.Should().Be(1);
        f.Saved!.Budgets.Should().BeEmpty();
    }

    [Fact]
    public async Task QuietCloseCancelsSourceAndRetirementPreservesOtherReceiptsAndGlobalBudget()
    {
        using var f = new Fixture();
        var broker = f.Create();
        await f.Observe(broker);
        var budget = f.Saved!.Budgets;
        await ChangeQuiet(f, broker, true);
        var other = f.Event() with { SessionId = new(Guid.NewGuid()) };
        other = other with { Id = LocalEvent.Identity(other.Source, other.SessionId.Value, other.SubjectId) };
        f.Saved = f.Saved! with { Schema = 2, Receipts = [.. f.Saved!.Receipts,
            new(other, LocalEventDisposition.RoutineSuppressed, null, f.Time.Now)] };
        await broker.DisposeAsync();
        broker = f.Create();
        await ChangeQuiet(f, broker, true);
        await broker.RetireSessionAsync(f.Session, f.Token);
        f.Saved!.Receipts.Should().ContainSingle().Which.Event.SessionId.Should().Be(other.SessionId);
        f.Saved.Budgets.Should().BeEquivalentTo(budget);
        broker.RoutineQuiet.Enabled.Should().BeTrue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var change = ChangeQuiet(f, broker, false);
        await entered.Task;
        await broker.DisposeAsync();
        var cancelled = () => change;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var closed = () => ChangeQuiet(f, broker, true);
        await closed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(LocalEventOperation.Dismiss)]
    [InlineData(LocalEventOperation.Defer)]
    public async Task QuietAdmissionRevokesInFlightExactActionsBeforeAnyDispositionCommit(LocalEventOperation operation)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        var initial = await f.Observe(broker);
        var target = initial.Events.Single().Event;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var exact = broker.ExecuteAsync(new(operation, target.Id, target.Revision), RequestOrigin.LocalUi, () => true, f.Token);
        await entered.Task;
        var quiet = ChangeQuiet(f, broker, true);
        broker.IsCurrent(initial).Should().BeFalse();
        release.SetResult();
        var action = () => exact;
        await action.Should().ThrowAsync<InvalidOperationException>();
        await quiet;
        f.Saved!.Receipts.Single().Disposition.Should().Be(LocalEventDisposition.Presented);
        f.Saved.Receipts.Single().DeferredUntil.Should().BeNull();
        f.Saved.Budgets.Single().Count.Should().Be(1);
        f.Audits.Should().NotContain(item => item.Event.ActionId == "local-event.dismiss" || item.Event.ActionId == "local-event.defer");
    }

    [Theory]
    [InlineData(LocalEventType.Failed)]
    [InlineData(LocalEventType.Blocked)]
    [InlineData(LocalEventType.Unknown)]
    public async Task ANewRequiredSourceRevisionIsNeverMutedByPriorRoutineSuppression(LocalEventType type)
    {
        using var f = new Fixture();
        await using var broker = f.Create();
        await ChangeQuiet(f, broker, true);
        var prior = f.Events.Single();
        f.Events = [prior with { SourceRevision = 2, Type = type }];
        var required = await f.Observe(broker);
        required.Events.Should().ContainSingle().Which.Reason.Should().Be(LocalEventReason.Eligible);
        required.RoutineOmitted.Should().Be(0);
        required.RoutineSuppressed.Should().Be(0);
        f.Saved!.Receipts.Single().Disposition.Should().Be(LocalEventDisposition.Presented);
        f.Saved.Budgets.Single().Category.Should().Be(LocalEventCategory.Failure);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("unconfirmed")]
    [InlineData("missing-member")]
    public async Task QuietControlNeverActivatesOverInvalidOrUnconfirmedSuppressionHistory(string boundary)
    {
        using var f = new Fixture();
        f.Saved = LocalEventBrokerState.Empty(f.Time.Now) with { Schema = boundary is "corrupt" ? 99 : 1 };
        if (boundary is "unconfirmed" or "missing-member") { f.BeginWrite(); }
        if (boundary is "missing-member") { f.Saved = null; }
        await using var broker = f.Create();
        var change = () => ChangeQuiet(f, broker, true);
        await change.Should().ThrowAsync<InvalidDataException>();
        broker.RoutineQuiet.Enabled.Should().BeFalse();
        broker.IsAvailable.Should().BeFalse();
        f.Saves.Should().Be(0);
        f.Audits.Should().BeEmpty();
    }
}
