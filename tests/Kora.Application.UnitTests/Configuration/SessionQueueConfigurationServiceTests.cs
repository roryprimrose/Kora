using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SessionQueueConfigurationServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public async Task ActiveBudgetActivationAndResetAreAuditedConfirmedAndFutureOnly(int minutes)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        await f.Set("120", SessionQueueOption.PendingLifetimeMinutes);
        await f.Refresh();
        await f.Set(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture), SessionQueueOption.ActiveBudgetMinutes);
        f.Service.Get().Effective.Should().Be(new SessionQueueLimits(pendingLifetimeMinutes: 120, activeBudgetMinutes: minutes));
        f.AuditLog.Events.TakeLast(2).Should().OnlyContain(item => item.ActionId == "configuration.queue-active-budget-minutes");
        await using var restart = f.Create();
        restart.Observe();
        restart.Get().Effective.Should().Be(f.Service.Get().Effective);
        await f.Refresh();
        await f.Set(null, SessionQueueOption.ActiveBudgetMinutes);
        f.Service.Get().Saved.Should().Be(new SessionQueuePreferences(pendingLifetimeMinutes: 120));
        f.Service.Get().Effective!.ActiveBudgetMinutes.Should().Be(5);
    }
    [Fact]
    public async Task Defaults_exact_bounds_independent_reset_and_audited_confirmed_restart()
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Get().Source.Should().Be("unavailable");
        SessionQueueConfigurationState.Serialize(f.Service.Get(), 1, "unavailable").Should().Contain("\"effective\":null");
        f.Service.Observe();
        f.Service.Observe();
        f.Service.Get().Effective.Should().Be(new SessionQueueLimits(10, 1));
        await f.Refresh();
        (await f.Set("1")).Should().BeTrue();
        await f.Refresh();
        await f.Set("2", SessionQueueOption.ExecutionSlots);
        await using var restart = f.Create();
        restart.Observe();
        restart.Get().Effective.Should().Be(new SessionQueueLimits(1, 2));
        restart.Get().Source.Should().Be("saved");
        await f.Refresh();
        await f.Set(null);
        f.Service.Get().Effective.Should().Be(new SessionQueueLimits(10, 2));
        await f.Refresh();
        await f.Set(null, SessionQueueOption.ExecutionSlots);
        f.Service.Get().Source.Should().Be("default");
        f.AuditLog.Events.Should().HaveCount(8);
        f.AuditLog.Events.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        f.Service.IsCurrent(f.Service.Get().Effective!).Should().BeTrue();
        f.Service.IsCurrent(new()).Should().BeFalse();
        SessionQueueConfigurationState.Serialize(f.Service.Get(), 1, "observed").Should()
            .Contain("\"schema\":3").And.Contain("\"minimum\":1").And.Contain("\"maximum\":10")
            .And.Contain("\"maximum\":2").And.Contain("\"maximum\":120").And.Contain("\"default\":30")
            .And.Contain("\"maximum\":60").And.Contain("\"default\":5").And.Contain("current-task extension");
        var tooLarge = () => SessionQueueConfigurationState.Serialize(f.Service.Get() with { Recovery = new('x', 65536) }, 1, "observed");
        tooLarge.Should().Throw<InvalidDataException>();
        var effective = f.Service.Get().Effective!;
        f.AuditLog.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested) { f.Service.IsCurrent(effective).Should().BeFalse(); }
        };
        await f.Refresh();
        await f.Set("1");
        f.Service.HoldUnavailable();
        f.Service.IsCurrent(effective).Should().BeFalse();
        await f.Service.DisposeAsync();
        f.Service.IsCurrent(effective).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public async Task Captured_lifetime_confirmation_is_audited_future_only_and_reset_is_independent(int minutes)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        await f.Set("2", SessionQueueOption.ExecutionSlots);
        await f.Refresh();
        await f.Set(minutes.ToString(System.Globalization.CultureInfo.InvariantCulture), SessionQueueOption.PendingLifetimeMinutes);
        f.Service.Get().Effective.Should().Be(new SessionQueueLimits(10, 2, minutes));
        f.AuditLog.Events.TakeLast(2).Should().OnlyContain(item => item.ActionId == "configuration.queue-pending-lifetime-minutes");
        await using var restart = f.Create();
        restart.Observe();
        restart.Get().Effective.Should().Be(f.Service.Get().Effective);
        await f.Refresh();
        await f.Set(null, SessionQueueOption.PendingLifetimeMinutes);
        f.Service.Get().Saved.Should().Be(new SessionQueuePreferences(null, 2));
        f.Service.Get().Effective!.PendingLifetimeMinutes.Should().Be(30);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("origin")]
    [InlineData("host")]
    [InlineData("saved")]
    [InlineData("hold")]
    [InlineData("initiator")]
    [InlineData("voice-origin")]
    public async Task Stale_proposals_original_input_and_private_ownership_fail_closed(string stage)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        var candidate = f.Propose("1", SessionQueueOption.PendingLifetimeMinutes);
        await using var other = f.Create();
        if (stage is "foreign") { await other.RefreshAsync(RequestOrigin.LocalUi, () => true, f.Token); candidate = other.Propose(SessionQueueOption.PendingPerSession, "1", other.Get().Revision, f.Policy.Current.Revision); }
        if (stage is "lookalike") { candidate = new(candidate.Value, candidate.Option, candidate.Revision, candidate.CallRevision, candidate.Session, candidate.Origin, candidate.Eligible); }
        if (stage is "revision") { await f.Refresh(); }
        if (stage is "session") { f.Store.Authority = f.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (stage is "generation") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (stage is "host") { f.Eligible = false; }
        if (stage is "saved") { f.PreferencesStore.Value = new(2); }
        if (stage is "hold") { f.Service.HoldUnavailable(); }
        var apply = () => f.Apply(candidate, stage is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            stage is "initiator" ? SecurityAuditInitiator.ModelSuggestion : stage is "voice-origin" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand);
        await apply.Should().ThrowAsync<InvalidOperationException>();
        f.PreferencesStore.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("invalid")]
    [InlineData("begin")]
    [InlineData("readback")]
    [InlineData("requested")]
    [InlineData("requested-state")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("late-readback")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    [InlineData("late-host")]
    [InlineData("confirmed-host")]
    public async Task Failed_writes_readback_audit_receipts_and_late_admission_never_activate_or_claim_rollback(string stage)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        f.PreferencesStore.WriteFailure = stage switch { "io" => new IOException(), "access" => new UnauthorizedAccessException(),
            "invalid" => new InvalidDataException(), _ => null };
        f.PreferencesStore.BeginFailure = stage is "begin" ? new IOException() : null;
        f.PreferencesStore.AfterWrite = () => { if (stage is "readback") { f.PreferencesStore.Value = new(2); } };
        f.AuditLog.BeforeWrite = item =>
        {
            if (stage is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || stage is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
            if (stage is "requested-state" && item.Outcome == SecurityAuditOutcome.Requested) { f.PreferencesStore.Value = new(2); }
        };
        f.Store.FailTerminal = stage is "receipt";
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (stage is "late-readback") { f.PreferencesStore.Value = new(2); }
            if (stage is "late-host") { f.Eligible = false; }
        };
        f.PreferencesStore.ConfirmFailure = stage is "confirm" ? new IOException() : null;
        f.PreferencesStore.AfterConfirm = () =>
        {
            if (stage is "confirmed-readback") { f.PreferencesStore.Value = new(2); }
            if (stage is "confirmed-load") { f.PreferencesStore.ReadFailure = new IOException(); }
            if (stage is "confirmed-host") { f.Eligible = false; }
        };
        var apply = () => f.Set("1", SessionQueueOption.PendingLifetimeMinutes);
        await apply.Should().ThrowAsync<Exception>();
        f.Service.Get().Available.Should().BeFalse();
        f.Service.Get().Effective.Should().BeNull();
        if (stage is not ("requested" or "requested-state" or "begin")) { f.PreferencesStore.Pending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task Protected_original_voice_denies_without_downgrade_or_deferred_write(CallState state)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh(RequestOrigin.ActivatedVoice);
        f.Call.Set(state);
        (await f.Apply(f.Propose("120", SessionQueueOption.PendingLifetimeMinutes), RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        f.PreferencesStore.Writes.Should().Be(0);
        f.PreferencesStore.Pending.Should().BeFalse();
        f.AuditLog.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.AuditLog.Events.Last().Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
    }

    [Fact]
    public async Task Original_activity_origin_call_revision_cancellation_and_unknown_session_hold()
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        var candidate = f.Propose("1");
        f.Call.Set(CallState.Active);
        (await f.Apply(candidate)).Should().BeFalse();
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            var relabelled = () => f.Refresh();
            await relabelled.Should().ThrowAsync<InvalidOperationException>();
        }
        var cancelled = () => f.Service.RefreshAsync(RequestOrigin.LocalUi, () => true, new CancellationToken(true));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await f.Refresh();
        using var late = new CancellationTokenSource();
        f.PreferencesStore.AfterWrite = late.Cancel;
        (await f.Service.ApplyAsync(f.Propose("1"), RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand,
            f.Policy, () => f.Eligible, late.Token)).Should().BeTrue();
        f.Service.Get().Effective!.PendingPerSession.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_changes_after_required_receipt_are_unavailable_not_defaults(bool revoke)
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        await f.Refresh();
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (revoke) { f.Eligible = false; }
            else { f.PreferencesStore.Value = new(1); }
        };
        var refresh = () => f.Refresh();
        await refresh.Should().ThrowAsync<InvalidOperationException>();
        f.Service.Get().Available.Should().BeFalse();
    }

    [Fact]
    public async Task Consuming_transaction_serializes_edits_and_revalidates_saved_state_revision_and_cancellation()
    {
        await using var f = new SessionQueueConfigurationTestFixture();
        var unavailable = () => f.Service.WithLimitsAsync(current => Task.FromResult(current), f.Token);
        await unavailable.Should().ThrowAsync<InvalidOperationException>();
        await f.Refresh();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = f.Service.WithLimitsAsync(async current => { entered.SetResult(); await release.Task; return current; }, f.Token);
        await entered.Task;
        var observe = () => f.Service.Observe();
        observe.Should().Throw<InvalidOperationException>();
        var edit = f.Set("1", SessionQueueOption.PendingLifetimeMinutes);
        edit.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await observation).PendingPerSession.Should().Be(10);
        (await observation).PendingLifetimeMinutes.Should().Be(30);
        await edit;
        (await f.Service.WithLimitsAsync(current => Task.FromResult(current), f.Token)).PendingLifetimeMinutes.Should().Be(1);
        var heldDuringRead = () => f.Service.WithLimitsAsync(current => { f.Service.HoldUnavailable(); return Task.FromResult(current); }, f.Token);
        await heldDuringRead.Should().ThrowAsync<InvalidOperationException>();
        await f.Refresh();
        var changedDuringRead = () => f.Service.WithLimitsAsync(current => { f.PreferencesStore.Value = new(2); return Task.FromResult(current); }, f.Token);
        await changedDuringRead.Should().ThrowAsync<InvalidOperationException>();
        f.Service.Get().Available.Should().BeFalse();
        await f.Refresh();
        f.PreferencesStore.ReadFailure = new IOException();
        var corrupt = () => f.Service.WithLimitsAsync(current => Task.FromResult(current), f.Token);
        await corrupt.Should().ThrowAsync<IOException>();
        f.Service.Get().Available.Should().BeFalse();
        f.PreferencesStore.ReadFailure = null;
        await f.Refresh();
        using var cancellation = new CancellationTokenSource();
        var cancelledRead = () => f.Service.WithLimitsAsync(current => { cancellation.Cancel(); return Task.FromResult(current); }, cancellation.Token);
        await cancelledRead.Should().ThrowAsync<OperationCanceledException>();
        await f.Service.DisposeAsync();
        await f.Service.DisposeAsync();
    }
}
