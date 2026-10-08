using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Communication;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Actual_typed_commands_share_all_direct_appearance_controls_and_live_notifications_without_reasoning()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var notified = new HashSet<string?>(StringComparer.Ordinal);
        fixture.ViewModel.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        foreach (var descriptor in fixture.ViewModel.AppearanceOptions)
        {
            var text = descriptor.Default switch
            {
                AppearanceValue.Theme => "dark",
                AppearanceValue.Toggle => "false",
                _ => descriptor.Maximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            await fixture.RunAsync($"Kora, set {descriptor.Id} to {text}");
            fixture.ViewModel.State.Should().Be(AssistantState.Success);
            fixture.ViewModel.ResponseBody.Should().Contain($"{descriptor.Id} = {text}");
            fixture.ViewModel.SelectedAppearanceOption = descriptor;
            await fixture.ViewModel.ResetAppearanceOptionCommand.ExecuteAsync();
            fixture.ViewModel.ResponseBody.Should().Contain($"{descriptor.Id} = {AppearanceCommand.Format(descriptor.Default)}");
        }
        notified.Should().Contain(nameof(fixture.ViewModel.ThemeMode))
            .And.Contain(nameof(fixture.ViewModel.IsPresenceDisplayEnabled))
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
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.PresenceSizePixels.Should().Be(PresenceSettings.DefaultSizePixels);
        fixture.ViewModel.PresenceDisplayDescription.Should().Contain("Show the animated presence");
        fixture.ViewModel.IsPresenceDisplayEnabled = false;
        fixture.ViewModel.IsPresenceDisplayEnabled.Should().BeFalse();
        fixture.ViewModel.PresenceDisplayDescription.Should().Contain("hidden");
        fixture.AppearancePreferences.SavedPresenceDisplayEnabled.Should().BeFalse();
        fixture.ViewModel.SetPresenceDisplayEnabled(true).Should().BeTrue();
        fixture.ViewModel.IsPresenceDisplayEnabled.Should().BeTrue();
        fixture.ViewModel.PresenceSizePixels = 400;
        fixture.ViewModel.PresenceDotSizePercent = 120;
        fixture.ViewModel.PresenceMovementSpeedPercent = 120;
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
        await fixture.ViewModel.ResetAppearanceOptionCommand.ExecuteAsync();
        await fixture.ViewModel.ExecuteAppearanceCommandAsync(
            new AppearanceCommand(AppearanceCommandOperation.List), SecurityAuditInitiator.TypedCommand);
        fixture.AppearancePreferences.SavedMode.Should().BeNull();

        var closing = await Fixture.CreateInitializedAsync();
        closing.Dispatcher.BeforePost = closing.ViewModel.Dispose;
        closing.ViewModel.SetThemeMode(ApplicationThemeMode.Dark).Should().BeTrue();
        closing.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Dark);
        closing.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
    }
}
