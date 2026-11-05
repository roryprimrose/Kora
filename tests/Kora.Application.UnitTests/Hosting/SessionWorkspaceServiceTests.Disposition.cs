using AwesomeAssertions;

using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("origin")]
    [InlineData("missing")]
    [InlineData("revoked")]
    public async Task CompoundCommitRequiresOriginalNativeAuthorityExactWorkAndPhysicalAdmission(string fault)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await f.Service.PreviewDispositionAsync(f.Session.SessionId, new(1), 0, f.Token);
        if (fault is not "missing")
        {
            var message = new Kora.Core.Conversation.ConversationMessage(f.Request, new(1), 1, 2,
                DateTimeOffset.UnixEpoch, "original", null, null);
            preview = preview with { StoppedWork = new(new(Guid.NewGuid(), f.Request, new(1), new(2),
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1)), message) };
        }
        var act = async () => await f.Service.CommitStoppedDispositionAsync(preview,
            fault is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            () => fault is not "revoked", f.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }
    [Theory]
    [InlineData("current")]
    [InlineData("foreign")]
    [InlineData("inspect")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("discard")]
    public async Task DispositionPreviewCurrencyAndExactDiscardNeverRetargetAnotherPendingReview(string mode)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await f.Service.PreviewDispositionAsync(f.Session.SessionId, new(1), 0, f.Token);
        var foreign = preview with { ConfirmationId = Guid.NewGuid() };
        f.Service.DiscardDispositionPreview(foreign);
        f.Service.IsDispositionPreviewCurrent(preview).Should().BeTrue();
        if (mode is "foreign") { preview = foreign; }
        if (mode is "inspect") { f.CanInspect = false; }
        if (mode is "control") { f.CanControl = false; }
        if (mode is "revision") { f.AdvanceRevision(); }
        if (mode is "discard")
        {
            f.Service.DiscardDispositionPreview(preview);
            f.Service.DiscardDispositionPreview(preview);
        }
        f.Service.IsDispositionPreviewCurrent(preview).Should().Be(mode is "current");
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplacedPreviewCannotReviveAfterALaterPreviewCompletesOrIsDiscarded()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var held = new TaskCompletionSource<SessionDispositionPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ReadDispositionPreview = () => new(held.Task);
        var first = f.Service.PreviewDispositionAsync(f.Session.SessionId, new(1), 0, f.Token);
        f.ReadDispositionPreview = null;
        var later = await f.Service.PreviewDispositionAsync(f.Session.SessionId, new(1), 0, f.Token);
        f.Service.DiscardDispositionPreview(later);
        held.SetResult(later with { ConfirmationId = Guid.NewGuid() });
        await first.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>().WithMessage("*replaced*");
        f.Service.IsDispositionPreviewCurrent(later).Should().BeFalse();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAfterPreviewReadCannotInstallPendingNativeConfirmation()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        f.AfterPreview = cancellation.Cancel;
        var act = () => f.Service.PreviewDispositionAsync(f.Session.SessionId, new(1), 0, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Committed_logical_disposition_retires_exact_owned_broker_suppression_without_replaying_or_extending_work()
    {
        using var fixture = new Fixture();
        using var events = new LocalEventBrokerTests.Fixture { Session = fixture.Session.SessionId };
        events.Events = [events.Event()];
        await using var broker = events.Create();
        await events.Observe(broker);
        fixture.Service.BindLocalEvents(broker);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        var receipt = await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        receipt.SessionId.Should().Be(events.Session);
        fixture.TaskWrites.Should().HaveCount(2);
        events.Saved!.Receipts.Should().BeEmpty();
        events.Audits.Should().Contain(item => item.Event.ActionId == "local-event.retire");
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("notification")]
    [InlineData("save")]
    public async Task CallerCancellationAfterDispositionCommitCannotInterruptExactNoticeCleanup(string boundary)
    {
        using var fixture = new Fixture();
        using var events = new LocalEventBrokerTests.Fixture { Session = fixture.Session.SessionId };
        var target = events.Event();
        var other = events.Event() with { SessionId = new(Guid.NewGuid()) };
        other = other with { Id = LocalEvent.Identity(other.Source, other.SessionId.Value, other.SubjectId) };
        events.Saved = LocalEventBrokerState.Empty(events.Time.Now) with
        {
            Receipts = [new(target, LocalEventDisposition.Presented, null, events.Time.Now),
                new(other, LocalEventDisposition.Dismissed, null, events.Time.Now)]
        };
        events.Events = [target];
        await using var broker = events.Create();
        await events.Observe(broker);
        var otherReceipt = events.Saved.Receipts.Single(item => item.Event.SessionId == other.SessionId);
        var budgets = events.Saved.Budgets;
        var sourceReads = 0;
        events.BeforeRead = _ => { sourceReads++; return Task.CompletedTask; };
        fixture.Service.BindLocalEvents(broker);
        using var cancellation = new CancellationTokenSource();
        fixture.BeforeTaskWrite = record =>
        {
            if (boundary is "terminal" && record.State == HostTaskState.Succeeded) { cancellation.Cancel(); }
        };
        HostId<SessionIdentity>? retired = null;
        fixture.Service.SessionRetired += session =>
        {
            retired = session;
            if (boundary is "notification") { cancellation.Cancel(); }
        };
        events.OnSave = () =>
        {
            if (boundary is "save") { cancellation.Cancel(); }
        };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        var receipt = await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, cancellation.Token);

        cancellation.IsCancellationRequested.Should().BeTrue();
        receipt.SessionId.Should().Be(target.SessionId);
        receipt.Removed.Should().Be(preview);
        retired.Should().Be(target.SessionId);
        fixture.ControlCalls.Should().Be(1);
        fixture.TaskWrites.Select(item => item.State).Should().Equal(HostTaskState.IntentRecorded, HostTaskState.Succeeded);
        events.Saved!.Receipts.Should().ContainSingle().Which.Should().Be(otherReceipt);
        events.Saved.Budgets.Should().Equal(budgets);
        events.Pending.Should().BeFalse();
        broker.IsAvailable.Should().BeTrue();
        broker.IsHeld(target).Should().BeFalse();
        sourceReads.Should().Be(0);
        events.Audits.Where(item => string.Equals(item.Event.ActionId, "local-event.retire", StringComparison.Ordinal))
            .Select(item => item.Event.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        await fixture.Service.Invoking(service => service.ConfirmDispositionAsync(preview,
            RequestOrigin.LocalUi, () => true, fixture.Token)).Should().ThrowAsync<InvalidOperationException>();
        fixture.ControlCalls.Should().Be(1);
    }

    [Fact]
    public async Task PrecommitCancellationDoesNotDeleteOrRetireNoticeState()
    {
        using var fixture = new Fixture();
        using var events = new LocalEventBrokerTests.Fixture { Session = fixture.Session.SessionId };
        events.Events = [events.Event()];
        await using var broker = events.Create();
        await events.Observe(broker);
        fixture.Service.BindLocalEvents(broker);
        var saved = events.Saved;
        var saves = events.Saves;
        var audits = events.Audits.Count;
        var retirements = 0;
        fixture.Service.SessionRetired += _ => retirements++;
        using var cancellation = new CancellationTokenSource();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        cancellation.Cancel();
        await fixture.Service.Invoking(service => service.ConfirmDispositionAsync(preview,
            RequestOrigin.LocalUi, () => true, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        fixture.ControlCalls.Should().Be(0);
        fixture.TaskWrites.Should().BeEmpty();
        retirements.Should().Be(0);
        events.Saved.Should().BeSameAs(saved);
        events.Saves.Should().Be(saves);
        events.Audits.Should().HaveCount(audits);
        broker.IsHeld(events.Events.Single()).Should().BeTrue();
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("storage")]
    [InlineData("audit")]
    public async Task CommittedDispositionStillSurfacesNoticeCleanupFailureWithoutReplay(string failure)
    {
        using var fixture = new Fixture();
        using var events = new LocalEventBrokerTests.Fixture { Session = fixture.Session.SessionId };
        events.Events = [events.Event()];
        await using var broker = events.Create();
        await events.Observe(broker);
        fixture.Service.BindLocalEvents(broker);
        using var cancellation = new CancellationTokenSource();
        fixture.Service.SessionRetired += _ =>
        {
            cancellation.Cancel();
            if (failure is "authority") { events.CanControl = false; }
        };
        events.OnSave = () =>
        {
            if (failure is "storage") { throw new IOException("Notice state write failed."); }
        };
        events.OnAudit = entry =>
        {
            if (failure is "audit" && string.Equals(entry.ActionId, "local-event.retire", StringComparison.Ordinal)
                && entry.Outcome == SecurityAuditOutcome.Requested)
            { throw new IOException("Notice retirement audit failed."); }
        };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        var confirm = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, cancellation.Token);
        if (failure is "authority") { await confirm.Should().ThrowAsync<InvalidOperationException>(); }
        else { await confirm.Should().ThrowAsync<IOException>(); }

        fixture.ControlCalls.Should().Be(1);
        fixture.TaskWrites.Select(item => item.State).Should().Equal(HostTaskState.IntentRecorded, HostTaskState.Succeeded);
        broker.IsHeld(events.Events.Single()).Should().BeFalse();
        events.Audits.Should().NotContain(item => string.Equals(item.Event.ActionId, "local-event.retire", StringComparison.Ordinal)
            && item.Event.Outcome == SecurityAuditOutcome.Succeeded);
        await fixture.Service.Invoking(service => service.ConfirmDispositionAsync(preview,
            RequestOrigin.LocalUi, () => true, fixture.Token)).Should().ThrowAsync<InvalidOperationException>();
        fixture.ControlCalls.Should().Be(1);
    }

    [Fact]
    public async Task Disposition_requires_host_held_preview_and_commits_only_one_exact_original_control()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        HostId<SessionIdentity>? retired = null;
        fixture.Service.SessionRetired += session => retired = session;
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        fixture.TaskWrites.Should().BeEmpty();
        var receipt = await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        receipt.SessionId.Should().Be(fixture.Session.SessionId);
        receipt.Generation.Value.Should().Be(2);
        retired.Should().Be(receipt.SessionId);
        receipt.Removed.Should().Be(preview);
        fixture.TaskWrites.Should().HaveCount(2);
        fixture.TaskWrites[0].Request.Should().NotBe(fixture.Request);
        fixture.TaskWrites[0].Request.Origin.Should().Be(RequestOrigin.LocalUi);
        fixture.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        var replay = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*live native*");
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("gate")]
    [InlineData("revision")]
    [InlineData("admission")]
    public async Task Unadmitted_or_stale_confirmations_never_record_intent(string mode)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        if (string.Equals(mode, "missing", StringComparison.Ordinal))
        {
            await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            fixture.TaskWrites.Clear();
        }
        if (string.Equals(mode, "foreign", StringComparison.Ordinal)) { preview = preview with { ConfirmationId = Guid.NewGuid() }; }
        if (string.Equals(mode, "gate", StringComparison.Ordinal)) { fixture.CanControl = false; }
        if (string.Equals(mode, "revision", StringComparison.Ordinal)) { fixture.AdvanceRevision(); }
        var act = () => fixture.Service.ConfirmDispositionAsync(preview,
            string.Equals(mode, "origin", StringComparison.Ordinal) ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            () => !string.Equals(mode, "admission", StringComparison.Ordinal), fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_gate_changes_fail_closed(bool revise)
    {
        using var fixture = new Fixture();
        fixture.AfterPreview = () =>
        {
            if (revise) { fixture.AdvanceRevision(); }
            else { fixture.CanControl = false; }
        };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var act = () => fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unchanged private*");
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Revision_change_during_resolution_denies_even_after_confirmation_consumed()
    {
        using var fixture = new Fixture { RevokeDuringDispositionResolution = true };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        var act = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_confirmation_arguments_are_explicit_errors()
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ConfirmDispositionAsync(null!, RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<ArgumentNullException>();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        act = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, null!, fixture.Token);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
