using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    [InlineData(CallState.Unavailable)]
    public async Task Call_activation_requires_separate_single_use_review_and_never_opens_capture(CallState state)
    {
        var f = await CallActivationFixture();
        f.CallState.SetState(state);
        await f.ViewModel.CallClosureTask;
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        f.ViewModel.CallVoiceActivationReviewStatus.Should().Contain("Off -> On").And.Contain(state.ToString());
        f.CallPreferences.SaveCalls.Should().Be(0);
        await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.SaveCalls.Should().Be(1);
        f.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(true, true));
        f.ViewModel.CallObservation.Settings.Should().Be(new CallAwareSettings(true, true));
        f.ViewModel.CallVoiceActivationReviewStatus.Should().StartWith("Saved");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        f.ViewModel.AreReusableGrantsIgnored.Should().BeFalse();
        f.Audit.Events.Where(e => string.Equals(e.ActionId, "configuration.call-aware-policy", StringComparison.Ordinal))
            .Select(e => e.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Call_activation_cancel_and_unreviewed_setter_cannot_relax_policy()
    {
        var f = await CallActivationFixture();
        await f.ViewModel.SetAllowVoiceActivationDuringCallsAsync(true);
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        await f.ViewModel.CancelCallVoiceActivationCommand.ExecuteAsync();
        f.ViewModel.CancelCallVoiceActivationCommand.Should().BeSameAs(f.ViewModel.CancelCallVoiceActivationCommand);
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Call_activation_review_expires_on_authority_input_and_native_lifetime_changes(int change)
    {
        var f = await CallActivationFixture();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        switch (change)
        {
            case 0: f.CallState.SetState(CallState.Suspected); await f.ViewModel.CallClosureTask; break;
            case 1: f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 1 }; break;
            case 2: owner++; break;
            case 3: f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case 4: await f.RunAsync("help"); break;
            case 5: f.ViewModel.BindCallVoiceActivationNativeLifetime(false); f.ViewModel.BindCallVoiceActivationNativeLifetime(true); break;
            case 6: f.Session.IsUnlocked = false; break;
            case 7: f.ViewModel.Dispose(); break;
            case 8: f.ViewModel.BindCallOwnershipGate(static () => true, () => owner); break;
        }
        try { await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync(); }
        catch (InvalidOperationException) { }
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.ViewModel.CallObservation.Settings.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
        f.Audit.Events.Should().NotContain(e => e.ActionId == "configuration.call-aware-policy" && e.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Call_activation_never_relabels_voice_or_unknown_origin_and_rejection_is_not_deferred(RequestOrigin origin)
    {
        var f = await CallActivationFixture();
        using (var ambient = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request))
        {
            await FluentActions.Awaiting(() => f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallState.SetState(CallState.Clear);
        await f.ViewModel.CallClosureTask;
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Call_activation_requires_native_lifetime_and_rejects_model_invocations_and_retired_context()
    {
        var f = await CallActivationFixture();
        f.ViewModel.BindCallVoiceActivationNativeLifetime(false);
        await FluentActions.Awaiting(() => f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.BindCallVoiceActivationNativeLifetime(true);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var invocation = HostActivity.BeginRoot(new(request.RequestId, request.SessionId, request.TaskId,
            RequestOrigin.LocalUi, new(Guid.NewGuid())), HostActivityLayer.Application, HostOperation.Tool))
        {
            await FluentActions.Awaiting(() => f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        using (var retired = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            retired.Activity!.Stop();
            await FluentActions.Awaiting(() => f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync())
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.CallPreferences.SaveCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Call_activation_revalidates_after_requested_audit_and_cancellation(bool cancel)
    {
        var f = await CallActivationFixture();
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        f.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, "configuration.call-aware-policy", StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            if (cancel) { f.ViewModel.CancelCallVoiceActivationReview(); }
            else { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 1 }; }
        };
        await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.ViewModel.CallVoiceActivationReviewStatus.Should().NotStartWith("Saved");
        f.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Denied);
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Call_activation_persistence_and_audit_failures_never_claim_success_or_retry(int failure)
    {
        var f = await CallActivationFixture();
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        if (failure == 0) { f.CallPreferences.SaveException = new IOException("Synthetic store failure."); }
        if (failure == 1) { f.CallPreferences.Settings = new(false, false); }
        if (failure == 2) { f.CallPreferences.Read = () => new(true, false); }
        if (failure is 3 or 4)
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, "configuration.call-aware-policy", StringComparison.Ordinal)
                    && audit.Outcome == (failure == 3 ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("Synthetic audit failure."); }
            };
        }
        try { await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync(); }
        catch (IOException) { }
        f.ViewModel.CallVoiceActivationReviewStatus.Should().Contain("not confirmed");
        f.Audit.Events.Should().NotContain(e => e.ActionId == "configuration.call-aware-policy" && e.Outcome == SecurityAuditOutcome.Succeeded);
        if (failure != 4) { f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse(); }
        var attempts = f.CallPreferences.SaveCalls;
        await FluentActions.Awaiting(() => f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SaveCalls.Should().Be(attempts);
        f.Voice.StartCalls.Should().Be(0);
        f.TextToSpeech.SpeakCalls.Should().Be(0);
    }

    [Fact]
    public async Task Call_activation_rechecks_after_store_read_and_after_save_without_claiming_rollback()
    {
        var f = await CallActivationFixture();
        f.CallPreferences.Read = () =>
        {
            f.ViewModel.BindCallVoiceActivationNativeLifetime(false);
            return f.CallPreferences.Settings;
        };
        await FluentActions.Awaiting(() => f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>();
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.CallPreferences.Read = null;
        f.ViewModel.BindCallVoiceActivationNativeLifetime(true);
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.BeforeSave = () => f.PrivacyObservation.Current =
            f.PrivacyObservation.Current with { TopologyRevision = 1 };
        await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.SavedSettings!.AllowVoiceActivationDuringCalls.Should().BeTrue();
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.ViewModel.CallVoiceActivationReviewStatus.Should().Contain("not confirmed");
        f.Audit.Events.Should().NotContain(e => e.Outcome == SecurityAuditOutcome.Succeeded);
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Call_activation_revalidates_immediately_after_final_saved_state_read_before_writing()
    {
        var f = await CallActivationFixture();
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.Read = () =>
        {
            f.ViewModel.CancelCallVoiceActivationReview();
            return f.CallPreferences.Settings;
        };
        await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync();
        f.CallPreferences.SaveCalls.Should().Be(0);
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        f.ViewModel.CallVoiceActivationReviewStatus.Should().Contain("not confirmed");
        f.Audit.Events.Should().Contain(e => e.Outcome == SecurityAuditOutcome.Denied);
        f.Audit.Events.Should().NotContain(e => e.Outcome == SecurityAuditOutcome.Succeeded);
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Reviewed_activation_preserves_manual_listening_hold_after_protected_call_closure()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.CallState.SetState(CallState.Active);
        await f.ViewModel.CallClosureTask;
        await f.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);
        var starts = f.Voice.StartCalls;
        f.ViewModel.BindCallVoiceActivationNativeLifetime(true);
        await f.ViewModel.ReviewCallVoiceActivationCommand.ExecuteAsync();
        await f.ViewModel.ConfirmCallVoiceActivationCommand.ExecuteAsync();
        f.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(starts);
        f.ViewModel.ShowVisualTextDuringCalls.Should().BeTrue();
        f.ViewModel.CallObservation.Settings.AllowSpokenApprovalsDuringCalls.Should().BeFalse();
        f.CallState.SetState(CallState.Clear);
        await f.ViewModel.CallClosureTask;
        f.Voice.StartCalls.Should().Be(starts);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
    }

    private static async Task<Fixture> CallActivationFixture()
    {
        var f = new Fixture();
        f.VoiceConsent.Consent = false;
        f.CallPreferences.Settings = new(true, false);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.CallVoiceActivationReviewStatus.Should().Contain("To restore");
        f.ViewModel.BindCallVoiceActivationNativeLifetime(true);
        f.Audit.Events.Clear();
        return f;
    }
}
