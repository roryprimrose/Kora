using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Communication;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task ReducedMotionAuditFailureNeverPromisesNoSavedEffectOrOffersUndo(SecurityAuditOutcome boundary)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, "configuration.reduced-motion", StringComparison.Ordinal) && audit.Outcome == boundary)
            { throw new IOException("Reduced motion audit unavailable."); }
        };
        fixture.ViewModel.IsReducedMotionEnabled = true;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("Reduced motion preference is not confirmed.");
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("not confirmed");
        fixture.AppearancePreferences.ReducedMotionEnabled.Should()
            .Be(boundary == SecurityAuditOutcome.Succeeded ? true : null);
        fixture.ViewModel.CanReviewSettingsUndo.Should().BeFalse();
        fixture.ViewModel.CanConfirmSettingsUndo.Should().BeFalse();
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task ReducedMotionReloadAndFailedDirectSaveKeepConfirmedChoiceAndTruthfulFailure()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.SaveReducedMotionEnabled(true);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsReducedMotionEnabled.Should().BeTrue();
        fixture.AppearancePreferences.SaveException = new IOException("Reduced motion save denied.");
        fixture.ViewModel.IsReducedMotionEnabled = false;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.IsReducedMotionEnabled.Should().BeTrue();
        fixture.AppearancePreferences.ReducedMotionEnabled.Should().BeTrue();
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("Reduced motion save denied.");
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.reduced-motion"
            && item.Outcome == SecurityAuditOutcome.Failed);
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.reduced-motion"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Renderer_get_distinguishes_unsaved_startup_choice_from_a_domain_default()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("get appearance.native-presence-renderer");
        fixture.ViewModel.ResponseBody.Should().Contain("Startup choice is effective; no saved preference.");
        await fixture.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task Renderer_selection_after_disposal_does_not_persist_or_change_the_choice()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.DisposeAsync();
        fixture.ViewModel.IsNativePresenceRenderer = true;
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
        fixture.AppearancePreferences.NativePresenceRendererEnabled.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RendererStartupReadHonorsSavedChoiceBeforeHostInitialization(bool native)
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.SaveNativePresenceRendererEnabled(native);
        fixture.ViewModel.IsNativePresenceRenderer.Should().Be(native);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedPresenceRenderer.IsNative.Should().Be(native);
        fixture.Audit.Events.Should().NotContain(item =>
            string.Equals(item.ActionId, "configuration.native-presence-renderer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Renderer_selector_uses_the_shared_audited_setting_and_does_not_claim_activation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.PresenceRenderers.Select(item => item.Name).Should()
            .Equal("Standard", "Experimental");
        var notifications = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[1];
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeTrue();
        fixture.AppearancePreferences.NativePresenceRendererEnabled.Should().BeTrue();
        notifications.Should().Contain(nameof(MainViewModel.IsNativePresenceRenderer))
            .And.Contain(nameof(MainViewModel.SelectedPresenceRenderer));
        fixture.ViewModel.PresenceRendererStatus.Should().NotContain("active.");
        fixture.ViewModel.ReportPresenceRendererStatus("Native renderer active.");
        fixture.ViewModel.PresenceRendererStatus.Should().Be("Native renderer active.");
        fixture.AppearancePreferences.SaveException = new IOException("fixture save failure");
        fixture.ViewModel.SelectedPresenceRenderer = fixture.ViewModel.PresenceRenderers[0];
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.SelectedPresenceRenderer.IsNative.Should().BeTrue();
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.native-presence-renderer"
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.native-presence-renderer"
            && item.Outcome == SecurityAuditOutcome.Failed);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Actual_typed_commands_share_all_direct_appearance_controls_and_live_notifications_without_reasoning()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var notified = new HashSet<string?>(StringComparer.Ordinal);
        fixture.ViewModel.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        var resets = new Dictionary<AppearanceOption, AsyncCommand>
        {
            [AppearanceOption.Theme] = fixture.ViewModel.ResetThemeCommand,
            [AppearanceOption.ReducedMotion] = fixture.ViewModel.ResetReducedMotionCommand,
            [AppearanceOption.ReaderTextScale] = fixture.ViewModel.ResetReaderTextScaleCommand,
            [AppearanceOption.ResponseTimeout] = fixture.ViewModel.ResetResponseTimeoutCommand,
            [AppearanceOption.PresenceDisplay] = fixture.ViewModel.ResetPresenceDisplayCommand,
            [AppearanceOption.NativePresenceRenderer] = fixture.ViewModel.ResetPresenceRendererCommand,
            [AppearanceOption.PresenceTimeout] = fixture.ViewModel.ResetPresenceTimeoutCommand,
            [AppearanceOption.PresenceSize] = fixture.ViewModel.ResetPresenceSizeCommand,
            [AppearanceOption.DotSize] = fixture.ViewModel.ResetPresenceDotSizeCommand,
            [AppearanceOption.DotDensity] = fixture.ViewModel.ResetPresenceDotDensityCommand,
            [AppearanceOption.MovementSpeed] = fixture.ViewModel.ResetPresenceMovementSpeedCommand,
            [AppearanceOption.SpeechScaling] = fixture.ViewModel.ResetPresenceSpeechScalingCommand,
            [AppearanceOption.SpeechScaleAmount] = fixture.ViewModel.ResetPresenceSpeechScaleAmountCommand,
        };
        foreach (var descriptor in fixture.ViewModel.AppearanceOptions)
        {
            var text = descriptor.Default switch
            {
                AppearanceValue.Theme => "dark",
                AppearanceValue.Toggle toggle => toggle.Value ? "false" : "true",
                _ => descriptor.Maximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            await fixture.RunAsync($"Kora, set {descriptor.Id} to {text}");
            fixture.ViewModel.State.Should().Be(AssistantState.Success);
            fixture.ViewModel.ResponseBody.Should().Contain($"{descriptor.Id} = {text}");
            await resets[descriptor.Option].ExecuteAsync();
            fixture.ViewModel.ResponseBody.Should().Contain($"{descriptor.Id} = {AppearanceCommand.Format(descriptor.Default)}");
        }
        notified.Should().Contain(nameof(fixture.ViewModel.ThemeMode))
            .And.Contain(nameof(fixture.ViewModel.IsReducedMotionEnabled))
            .And.Contain(nameof(fixture.ViewModel.IsPresenceDisplayEnabled))
            .And.Contain(nameof(fixture.ViewModel.IsNativePresenceRenderer))
            .And.Contain(nameof(fixture.ViewModel.PresenceTimeoutSeconds))
            .And.Contain(nameof(fixture.ViewModel.ResponseTimeoutSeconds))
            .And.Contain(nameof(fixture.ViewModel.PresenceSizePixels))
            .And.Contain(nameof(fixture.ViewModel.PresenceDotSizePercent))
            .And.Contain(nameof(fixture.ViewModel.PresenceDotDensityPercent))
            .And.Contain(nameof(fixture.ViewModel.PresenceMovementSpeedPercent))
            .And.Contain(nameof(fixture.ViewModel.IsPresenceSpeechScalingEnabled))
            .And.Contain(nameof(fixture.ViewModel.PresenceSpeechScaleAmountPercent));
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.ViewModel.IsPresenceDisplayEnabled.Should().BeTrue();
        fixture.ViewModel.IsNativePresenceRenderer.Should().BeFalse();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.PresenceSizePixels.Should().Be(PresenceSettings.DefaultSizePixels);
        fixture.ViewModel.PresenceDisplayDescription.Should().Contain("Show animated dots");
        fixture.ViewModel.IsPresenceDisplayEnabled = false;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.ViewModel.IsPresenceDisplayEnabled.Should().BeFalse();
        fixture.ViewModel.PresenceDisplayDescription.Should().Contain("Hide the animated dots");
        fixture.AppearancePreferences.SavedPresenceDisplayEnabled.Should().BeFalse();
        fixture.ViewModel.SetPresenceDisplayEnabled(true).Should().BeTrue();
        fixture.ViewModel.IsPresenceDisplayEnabled.Should().BeTrue();
        fixture.ViewModel.PresenceSizePixels = 400;
        fixture.ViewModel.PresenceDotSizePercent = 120;
        fixture.ViewModel.PresenceMovementSpeedPercent = 120;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.AppearancePreferences.SavedPresencePosition.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Fact]
    public async Task Activated_voice_uses_same_service_current_prefix_and_visual_only_effect_even_during_calls()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RaiseActivatedTranscriptAsync("Nova, set appearance theme to dark", 1);
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(item => item.ActionId == "configuration.appearance-theme"
            && item.Initiator == SecurityAuditInitiator.VoiceCommand && item.Outcome == SecurityAuditOutcome.Succeeded);
        await fixture.RaiseActivatedTranscriptAsync("Nova, reset appearance theme", 1);
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
    }

    [Fact]
    public async Task Discovery_get_rejection_and_failed_reset_do_not_reach_a_model_or_report_success()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("list appearance settings");
        fixture.ViewModel.ResponseBody.Should().Contain("appearance.theme").And.Contain("appearance.speech-scale-amount")
            .And.Contain("appearance-only").And.NotContain("call.audio");
        await fixture.RunAsync("get appearance.theme");
        fixture.ViewModel.ResponseBody.Should().Contain("system").And.Contain("revision");
        await fixture.RunAsync("reset appearance.theme");
        fixture.ViewModel.ResponseTitle.Should().Contain("unchanged");
        fixture.ViewModel.ResponseBody.Should().Contain("no saved preference");
        await fixture.RunAsync("set appearance.presence-timeout to -10");
        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        await fixture.RunAsync("reset appearance.audio");
        fixture.ViewModel.ResponseTitle.Should().Contain("Clarify");
        fixture.ViewModel.ThemeMode = ApplicationThemeMode.Dark;
        await fixture.ViewModel.AppearanceUpdate;
        fixture.AppearancePreferences.SaveException = new IOException("owned fixture failure");
        await fixture.RunAsync("reset appearance.theme");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("owned fixture failure");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_saved_domain_values_fail_with_InvalidDataException_and_no_partial_update()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var service = new AppearanceConfigurationService(fixture.AppearancePreferences, fixture.Audit,
            NullLogger<AppearanceConfigurationService>.Instance);
        service.Reload();
        fixture.AppearancePreferences.Mode = ApplicationThemeMode.Dark;
        fixture.AppearancePreferences.PresenceSizePixels = PresenceSettings.MaximumSizePixels + 1;
        var reload = service.Reload;
        reload.Should().Throw<InvalidDataException>();
        service.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.System));
    }

    [Fact]
    public async Task Typed_command_dispatch_rejects_missing_option_or_value_without_persisting()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var missingOption = () => fixture.ViewModel.ExecuteAppearanceCommandAsync(
            new AppearanceCommand(AppearanceCommandOperation.Set), SecurityAuditInitiator.TypedCommand);
        await missingOption.Should().ThrowAsync<InvalidOperationException>();
        var missingValue = () => fixture.ViewModel.ExecuteAppearanceCommandAsync(
            new AppearanceCommand(AppearanceCommandOperation.Set, AppearanceOptionRegistry.Get(AppearanceOption.Theme)),
            SecurityAuditInitiator.TypedCommand);
        await missingValue.Should().ThrowAsync<InvalidOperationException>();
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
    }

    [Fact]
    public async Task Appearance_dispatch_rechecks_host_lifecycle_and_queued_notifications_retire_on_disposal()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.BeforeInvoke = fixture.ViewModel.Dispose;
        await fixture.RunAsync("set appearance theme to dark");
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.ViewModel.AppearanceSettingStatus.Should().Contain("unlocked local host");
        fixture.ViewModel.SetThemeMode(ApplicationThemeMode.Dark).Should().BeFalse();
        await fixture.ViewModel.ResetThemeCommand.ExecuteAsync();
        await fixture.ViewModel.ExecuteAppearanceCommandAsync(
            new AppearanceCommand(AppearanceCommandOperation.List), SecurityAuditInitiator.TypedCommand);
        fixture.AppearancePreferences.SavedMode.Should().BeNull();

        var closing = await Fixture.CreateInitializedAsync();
        closing.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome == SecurityAuditOutcome.Succeeded)
            { closing.Dispatcher.BeforePost = closing.ViewModel.Dispose; }
        };
        closing.ViewModel.SetThemeMode(ApplicationThemeMode.Dark).Should().BeTrue();
        closing.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Dark);
        closing.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponseResetChangesOnlyItsOwnField(bool resetAlwaysShow)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var position = new ResponseWindowPosition(120, 240);
        await fixture.ViewModel.SetResponseAlwaysVisibleAsync(true);
        await fixture.ViewModel.SetResponseWindowTopmostAsync(false);
        await fixture.ViewModel.SetResponseWindowPositionAsync(position);
        var reset = resetAlwaysShow
            ? fixture.ViewModel.ResetResponseAlwaysVisibleCommand
            : fixture.ViewModel.ResetResponseWindowTopmostCommand;

        await reset.ExecuteAsync();

        fixture.ViewModel.IsResponseAlwaysVisible.Should().Be(!resetAlwaysShow);
        fixture.ViewModel.IsResponseWindowTopmost.Should().Be(!resetAlwaysShow);
        fixture.ViewModel.ResponseWindowPosition.Should().Be(position);
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(
            new ResponseWindowSettings(!resetAlwaysShow, !resetAlwaysShow, position));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponseResetReportsPersistenceFailureWithoutChangingValues(bool resetAlwaysShow)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.SetResponseAlwaysVisibleAsync(true);
        await fixture.ViewModel.SetResponseWindowTopmostAsync(false);
        fixture.AppearancePreferences.SaveException = new IOException("Reset could not be saved.");

        await (resetAlwaysShow
            ? fixture.ViewModel.ResetResponseAlwaysVisibleCommand
            : fixture.ViewModel.ResetResponseWindowTopmostCommand).ExecuteAsync();

        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeTrue();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("Reset could not be saved.");
    }
}
