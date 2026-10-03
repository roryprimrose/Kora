using System.Globalization;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.ViewModels;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsync_populates_readiness_selects_system_devices_and_starts_listening()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("0", "Headset"),
            new MicrophoneDevice("1", "Webcam"),
        ];
        fixture.Voice.DefaultMicrophoneId = "0";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.Microphones.Should().Equal(
            [SystemAudioDevices.Microphone, .. fixture.Voice.Microphones]);
        fixture.ViewModel.OutputDevices.Should().Equal(
            [SystemAudioDevices.Output, .. fixture.TextToSpeech.OutputDevices]);
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.Dependencies.Should().ContainSingle()
            .Which.Readiness.Should().Be(DependencyReadiness.Ready);
        fixture.Voice.StartCalls.Should().Be(1);
        fixture.Voice.StartedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.ViewModel.MicrophoneAccessStatus.State.Should().Be(MicrophoneAccessState.Allowed);
        fixture.ViewModel.MicrophoneAccessMessage.Should().Contain("allowed");
        fixture.ViewModel.ResponseTitle.Should().Be("I'm listening.");
        fixture.ViewModel.State.Should().Be(AssistantState.Listening);
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_does_not_start_listening_without_an_effective_microphone()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("No microphone detected.");
    }

    [Fact]
    public async Task InitializeAsync_does_not_start_listening_when_call_policy_blocks_activation()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.CallPreferences.Settings = new CallAwareSettings(true, false);
        fixture.CallState.SetState(CallState.Active);

        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Environment check complete.");
    }

    [Fact]
    public async Task InitializeAsync_does_not_start_listening_when_the_session_is_not_confirmed_unlocked()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.Session.IsUnlocked = false;

        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Listening is paused.");
        fixture.ViewModel.ResponseBody.Should().Contain("could not confirm");
        fixture.ViewModel.ListeningStatus.Should().Contain("session as locked");
    }

    [Fact]
    public async Task InitializeAsync_reports_when_Windows_microphone_access_is_blocked()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(
            MicrophoneAccessState.Denied,
            "Windows microphone access is blocked for desktop apps.");

        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsMicrophoneAccessDenied.Should().BeTrue();
        fixture.ViewModel.MicrophoneAccessMessage.Should().Contain("blocked");
        fixture.ViewModel.ListeningStatus.Should().Contain("access is blocked");
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone access is blocked.");
    }

    [Fact]
    public async Task Reinitializing_while_listening_does_not_start_capture_again()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";

        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(StartFailures))]
    public async Task InitializeAsync_surfaces_expected_automatic_listening_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.Voice.StartException = exception;

        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_uses_system_theme_by_default_without_saving()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.ViewModel.ThemeModes.Should().Equal(
            ApplicationThemeMode.System,
            ApplicationThemeMode.Light,
            ApplicationThemeMode.Dark);
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.PresenceTimeoutDescription.Should().Contain("Hide the constellation");
        fixture.ViewModel.PresenceTimeoutDescription.Should().Contain("unpinned response window");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeTrue();
        fixture.ViewModel.ResponseWindowPosition.Should().BeNull();
        fixture.ViewModel.ConstellationSizePixels.Should().Be(ConstellationSettings.DefaultSizePixels);
        fixture.ViewModel.ConstellationDotSizePercent.Should().Be(ConstellationSettings.DefaultDotSizePercent);
        fixture.ViewModel.ConstellationMovementSpeedPercent.Should()
            .Be(ConstellationSettings.DefaultMovementSpeedPercent);
        fixture.ViewModel.ConstellationPosition.Should().BeNull();
        fixture.ViewModel.ConstellationSizeDescription.Should().Contain("360 pixels");
        fixture.ViewModel.ConstellationDotSizeDescription.Should().Contain("100%");
        fixture.ViewModel.ConstellationMovementSpeedDescription.Should().Contain("100%");
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationSizePixels.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationDotSizePercent.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationMovementSpeedPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationPosition.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_loads_the_saved_theme_without_resaving_it()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.Mode = ApplicationThemeMode.Dark;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_loads_the_saved_presence_timeout_without_resaving_it()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.PresenceTimeoutSeconds = 10;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(10);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_loads_saved_constellation_settings_without_resaving_them()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.ConstellationSizePixels = 400;
        fixture.AppearancePreferences.ConstellationDotSizePercent = 120;
        fixture.AppearancePreferences.ConstellationMovementSpeedPercent = 125;
        fixture.AppearancePreferences.ConstellationPosition =
            new ConstellationPosition(320, -120);

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ConstellationSizePixels.Should().Be(400);
        fixture.ViewModel.ConstellationDotSizePercent.Should().Be(120);
        fixture.ViewModel.ConstellationMovementSpeedPercent.Should().Be(125);
        fixture.ViewModel.ConstellationPosition.Should().Be(
            new ConstellationPosition(320, -120));
        fixture.AppearancePreferences.SavedConstellationSizePixels.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationDotSizePercent.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationMovementSpeedPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedConstellationPosition.Should().BeNull();
    }

    [Fact]
    public async Task Constellation_position_changes_persist_notify_and_are_audited()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        var position = new ConstellationPosition(-640, 240);

        var changed = fixture.ViewModel.SetConstellationPosition(
            position,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetConstellationPosition(
            position,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.ConstellationPosition.Should().Be(position);
        fixture.AppearancePreferences.SavedConstellationPosition.Should().Be(position);
        changedProperties.Should().ContainSingle()
            .Which.Should().Be(nameof(MainViewModel.ConstellationPosition));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.constellation-position",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Constellation_position_rejects_null()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => fixture.ViewModel.SetConstellationPosition(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Constellation_position_save_failure_retains_previous_position(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        var changed = fixture.ViewModel.SetConstellationPosition(
            new ConstellationPosition(100, 200));

        changed.Should().BeFalse();
        fixture.ViewModel.ConstellationPosition.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be(
            "The constellation position could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.constellation-position",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Fact]
    public async Task InitializeAsync_loads_response_window_settings_without_resaving_them()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.ResponseWindowSettings = new ResponseWindowSettings(
            AlwaysShow: true,
            Topmost: false,
            Position: new ResponseWindowPosition(200, 300));

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeTrue();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeFalse();
        fixture.ViewModel.ResponseWindowPosition.Should().Be(new ResponseWindowPosition(200, 300));
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Fact]
    public async Task Theme_changes_persist_notify_and_use_the_supplied_audit_initiator()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = fixture.ViewModel.SetThemeMode(
            ApplicationThemeMode.Light,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetThemeMode(
            ApplicationThemeMode.Light,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.Light);
        fixture.AppearancePreferences.SavedMode.Should().Be(ApplicationThemeMode.Light);
        changedProperties.Should().ContainSingle()
            .Which.Should().Be(nameof(MainViewModel.ThemeMode));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.appearance-theme",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Theme_change_rejects_an_invalid_mode()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => fixture.ViewModel.ThemeMode = (ApplicationThemeMode)100;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Presence_timeout_changes_persist_notify_and_use_the_supplied_audit_initiator()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = fixture.ViewModel.SetPresenceTimeoutSeconds(
            10,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetPresenceTimeoutSeconds(
            10,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(10);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().Be(10);
        changedProperties.Should().ContainSingle()
            .Which.Should().Be(nameof(MainViewModel.PresenceTimeoutSeconds));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-timeout",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumTimeoutSeconds - 1)]
    [InlineData(PresenceSettings.MaximumTimeoutSeconds + 1)]
    public async Task Presence_timeout_change_rejects_an_invalid_value(int value)
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => fixture.ViewModel.PresenceTimeoutSeconds = value;

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Response_window_changes_persist_notify_and_are_audited()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var alwaysShowChanged = fixture.ViewModel.SetResponseAlwaysVisible(
            true,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetResponseAlwaysVisible(
            true,
            SecurityAuditInitiator.VoiceCommand);
        var topmostChanged = fixture.ViewModel.SetResponseWindowTopmost(
            false,
            SecurityAuditInitiator.VoiceCommand);
        var positionChanged = fixture.ViewModel.SetResponseWindowPosition(
            new ResponseWindowPosition(120, -80),
            SecurityAuditInitiator.VoiceCommand);

        alwaysShowChanged.Should().BeTrue();
        unchanged.Should().BeTrue();
        topmostChanged.Should().BeTrue();
        positionChanged.Should().BeTrue();
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeTrue();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeFalse();
        fixture.ViewModel.ResponseWindowPosition.Should().Be(new ResponseWindowPosition(120, -80));
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(
            new ResponseWindowSettings(
                AlwaysShow: true,
                Topmost: false,
                Position: new ResponseWindowPosition(120, -80)));
        changedProperties.Should().HaveCount(9);
        fixture.Audit.Events
            .Where(item => string.Equals(
                item.ActionId,
                "configuration.response-window",
                StringComparison.Ordinal))
            .Should()
            .HaveCount(6);
        fixture.Audit.Events
            .Where(item => string.Equals(
                item.ActionId,
                "configuration.response-window",
                StringComparison.Ordinal))
            .Should()
            .OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
    }

    [Fact]
    public async Task Response_window_bound_properties_persist_changes()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.IsResponseAlwaysVisible = true;
        fixture.ViewModel.IsResponseWindowTopmost = false;

        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(
            new ResponseWindowSettings(
                AlwaysShow: true,
                Topmost: false,
                Position: null));
    }

    [Fact]
    public async Task Response_window_position_rejects_null()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => fixture.ViewModel.SetResponseWindowPosition(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(
        ConstellationSetting.Size,
        400,
        nameof(MainViewModel.ConstellationSizePixels),
        nameof(MainViewModel.ConstellationSizeDescription),
        "configuration.constellation-size")]
    [InlineData(
        ConstellationSetting.DotSize,
        120,
        nameof(MainViewModel.ConstellationDotSizePercent),
        nameof(MainViewModel.ConstellationDotSizeDescription),
        "configuration.constellation-dot-size")]
    [InlineData(
        ConstellationSetting.MovementSpeed,
        125,
        nameof(MainViewModel.ConstellationMovementSpeedPercent),
        nameof(MainViewModel.ConstellationMovementSpeedDescription),
        "configuration.constellation-movement-speed")]
    public async Task Constellation_changes_persist_notify_and_use_the_supplied_audit_initiator(
        ConstellationSetting setting,
        int value,
        string propertyName,
        string descriptionPropertyName,
        string actionId)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = SetConstellationSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = SetConstellationSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        GetConstellationSetting(fixture.ViewModel, setting).Should().Be(value);
        GetSavedConstellationSetting(fixture.AppearancePreferences, setting).Should().Be(value);
        changedProperties.Should().Equal(propertyName, descriptionPropertyName);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            actionId,
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(ConstellationSetting.Size, ConstellationSettings.MinimumSizePixels - 1)]
    [InlineData(ConstellationSetting.Size, ConstellationSettings.MaximumSizePixels + 1)]
    [InlineData(ConstellationSetting.DotSize, ConstellationSettings.MinimumDotSizePercent - 1)]
    [InlineData(ConstellationSetting.DotSize, ConstellationSettings.MaximumDotSizePercent + 1)]
    [InlineData(ConstellationSetting.MovementSpeed, ConstellationSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(ConstellationSetting.MovementSpeed, ConstellationSettings.MaximumMovementSpeedPercent + 1)]
    public async Task Constellation_change_rejects_an_invalid_value(
        ConstellationSetting setting,
        int value)
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => SetConstellationSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.LocalUser);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Theme_save_failure_retains_the_previous_mode_and_is_audited(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        var changed = fixture.ViewModel.SetThemeMode(ApplicationThemeMode.Dark);

        changed.Should().BeFalse();
        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.ViewModel.ResponseTitle.Should().Be("The appearance theme could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.appearance-theme",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Presence_timeout_save_failure_retains_the_previous_value_and_is_audited(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        var changed = fixture.ViewModel.SetPresenceTimeoutSeconds(10);

        changed.Should().BeFalse();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.ResponseTitle.Should().Be("The presence timeout could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-timeout",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Response_window_save_failure_retains_previous_settings_and_is_audited(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        var changed = accessDenied
            ? fixture.ViewModel.SetResponseAlwaysVisible(true)
            : fixture.ViewModel.SetResponseWindowTopmost(false);

        changed.Should().BeFalse();
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("The response window settings could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-window",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Theory]
    [InlineData(
        ConstellationSetting.Size,
        true,
        "access-denied",
        "configuration.constellation-size",
        "The constellation size could not be saved.")]
    [InlineData(
        ConstellationSetting.Size,
        false,
        "io-error",
        "configuration.constellation-size",
        "The constellation size could not be saved.")]
    [InlineData(
        ConstellationSetting.DotSize,
        true,
        "access-denied",
        "configuration.constellation-dot-size",
        "The constellation dot size could not be saved.")]
    [InlineData(
        ConstellationSetting.DotSize,
        false,
        "io-error",
        "configuration.constellation-dot-size",
        "The constellation dot size could not be saved.")]
    [InlineData(
        ConstellationSetting.MovementSpeed,
        true,
        "access-denied",
        "configuration.constellation-movement-speed",
        "The constellation movement speed could not be saved.")]
    [InlineData(
        ConstellationSetting.MovementSpeed,
        false,
        "io-error",
        "configuration.constellation-movement-speed",
        "The constellation movement speed could not be saved.")]
    public async Task Constellation_save_failure_retains_the_previous_value_and_is_audited(
        ConstellationSetting setting,
        bool accessDenied,
        string reasonCode,
        string actionId,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");
        var previousValue = GetConstellationSetting(fixture.ViewModel, setting);

        var changed = SetConstellationSetting(
            fixture.ViewModel,
            setting,
            previousValue + 20,
            SecurityAuditInitiator.LocalUser);

        changed.Should().BeFalse();
        GetConstellationSetting(fixture.ViewModel, setting).Should().Be(previousValue);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            actionId,
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Fact]
    public async Task InitializeAsync_surfaces_an_invalid_saved_theme()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.LoadException =
            new InvalidDataException("invalid appearance theme");

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ThemeMode.Should().Be(ApplicationThemeMode.System);
        fixture.ViewModel.ResponseTitle.Should().Be("A saved setting is invalid.");
        fixture.ViewModel.ResponseBody.Should().Be("invalid appearance theme");
    }

    [Fact]
    public async Task InitializeAsync_loads_the_saved_name_without_resaving_it()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Nova";
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.AssistantName.Should().Be("Nova");
        fixture.ViewModel.AssistantInitial.Should().Be("n");
        fixture.ViewModel.SettingsWindowTitle.Should().Be("Nova settings");
        fixture.ViewModel.SettingsSubtitle.Should().Be("Nova preferences on this device");
        fixture.ViewModel.AppearanceSettingsDescription.Should().Contain("Nova");
        fixture.ViewModel.AppearanceThemeDescription.Should().Contain("Nova");
        fixture.ViewModel.MainCaptureDescription.Should().StartWith("Nova");
        fixture.ViewModel.SpeechAudioSettingsDescription.Should().Contain("Nova");
        fixture.ViewModel.ResponseSettingsDescription.Should().Contain("Nova");
        fixture.ViewModel.ReadinessSettingsDescription.Should().EndWith("Nova.");
        fixture.ViewModel.CommandText.Should().Be("Nova, what can you do?");
        fixture.ViewModel.MicrophoneAvailabilityMessage.Should().Contain("Nova capture");
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Nova playback");
        fixture.NamePreferences.SavedName.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_rejects_a_saved_name_that_conflicts_with_a_command()
    {
        var fixture = new Fixture();
        fixture.NamePreferences.Name = "Supported Commands";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.ResponseTitle.Should().Be("A saved setting is invalid.");
        fixture.ViewModel.ResponseBody.Should().Contain("conflicts with a built-in command");
    }

    [Fact]
    public async Task Applying_a_name_updates_identity_surfaces_routing_and_audit_without_logging_the_value()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.AssistantNameInput = "  Nova  Prime ";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();

        fixture.ViewModel.AssistantName.Should().Be("Nova Prime");
        fixture.ViewModel.AssistantNameInput.Should().Be("Nova Prime");
        fixture.ViewModel.CommandText.Should().Be("Nova Prime, what can you do?");
        fixture.ViewModel.ResponseTitle.Should().Be("Nova Prime is ready.");
        fixture.ViewModel.ResponseBody.Should().NotContain("Kora");
        fixture.ViewModel.Commands.Should().Contain(command =>
            command.CanonicalPhrase == "show nova prime");
        fixture.NamePreferences.SavedName.Should().Be("Nova Prime");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.assistant-name",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);

        await fixture.RunAsync("Kora, what can you do?");
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");

        await fixture.RunAsync("Nova Prime, what can you do?");
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        fixture.ViewModel.ResponseBody.Should()
            .Contain("Nova Prime, open documentation")
            .And.Contain("choose Commands")
            .And.NotContain("catalogue on the right");
    }

    [Fact]
    public async Task Applying_a_name_while_listening_restarts_capture_with_name_aware_grammar_and_preview()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(1);
        fixture.Voice.StartCalls.Should().Be(2);
        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.Voice.StartedPhrases.Should().Contain("Nova what can you do");
        fixture.Voice.StartedPhrases.Should().NotContain("Kora what can you do");

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        fixture.TextToSpeech.SpokenText.Should().Be("Hello, I'm Nova.");
    }

    [Fact]
    public async Task SetAssistantNameAsync_rejects_invalid_and_unchanged_names_with_audited_outcomes()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.SetAssistantNameAsync("Nova!");
        await fixture.ViewModel.SetAssistantNameAsync("Supported Commands");
        await fixture.ViewModel.SetAssistantNameAsync("Kora");

        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.ResponseTitle.Should().Be("The assistant name is invalid.");
        fixture.Audit.Events.Where(item => string.Equals(
                item.ActionId,
                "configuration.assistant-name",
                StringComparison.Ordinal))
            .Select(item => item.Outcome)
            .Should().Equal(
                SecurityAuditOutcome.Requested,
                SecurityAuditOutcome.Denied,
                SecurityAuditOutcome.Requested,
                SecurityAuditOutcome.Denied,
                SecurityAuditOutcome.Requested,
                SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task SetAssistantNameAsync_surfaces_and_audits_save_failures(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.NamePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        await fixture.ViewModel.SetAssistantNameAsync("Nova");

        fixture.ViewModel.AssistantName.Should().Be("Kora");
        fixture.ViewModel.ResponseTitle.Should().Be("The assistant name could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.assistant-name",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Fact]
    public async Task Name_input_reports_validation_and_preserves_non_default_typed_text()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.CommandText = "help";

        fixture.ViewModel.AssistantNameInput = " ";
        fixture.ViewModel.ApplyAssistantNameCommand.CanExecute(null).Should().BeFalse();

        fixture.ViewModel.AssistantNameInput = "Nova!";
        fixture.ViewModel.AssistantNameSettingStatus.Should().Contain("only letters");
        fixture.ViewModel.ApplyAssistantNameCommand.CanExecute(null).Should().BeTrue();

        fixture.ViewModel.AssistantNameInput = "Nova";
        fixture.ViewModel.AssistantNameSettingStatus.Should().Contain("Apply to use Nova");
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();

        fixture.ViewModel.CommandText.Should().Be("help");
        fixture.ViewModel.AssistantNameSettingStatus.Should().Be("The current name is Nova.");
        fixture.ViewModel.ApplyAssistantNameCommand.CanExecute(null).Should().BeFalse();

        fixture.ViewModel.AssistantNameInput = "Atlas";
        fixture.Probe.Gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.ApplyAssistantNameCommand.CanExecute(null).Should().BeFalse();
        fixture.Probe.Gate.SetResult();
        await refresh;
    }

    [Fact]
    public async Task First_run_selects_System_without_persisting_endpoint_snapshots()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("microphone-other", "Other microphone"),
            new MicrophoneDevice("microphone-default", "Windows microphone"),
        ];
        fixture.Voice.DefaultMicrophoneId = "microphone-default";
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-other", "Other speakers"),
            new AudioOutputDevice("output-default", "Windows speakers"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-default";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.MicrophoneAvailabilityMessage.Should().Contain("follows the Windows default");
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("follows the Windows default");
        fixture.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task System_selections_remain_dynamic_when_Windows_defaults_change()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("microphone-first", "First microphone"),
            new MicrophoneDevice("microphone-second", "Second microphone"),
        ];
        fixture.Voice.DefaultMicrophoneId = "microphone-first";
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-first", "First speakers"),
            new AudioOutputDevice("output-second", "Second speakers"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-first";
        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.DefaultMicrophoneId = "microphone-second";
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-second";
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.AudioPreferences.MicrophoneId.Should().BeNull();
        fixture.AudioPreferences.OutputDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Saved_device_overrides_take_precedence_over_Windows_defaults()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("microphone-saved", "Saved microphone"),
            new MicrophoneDevice("microphone-default", "Windows microphone"),
        ];
        fixture.Voice.DefaultMicrophoneId = "microphone-default";
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-saved", "Saved speakers"),
            new AudioOutputDevice("output-default", "Windows speakers"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-default";
        fixture.AudioPreferences.MicrophoneId = "microphone-saved";
        fixture.AudioPreferences.OutputDeviceId = "output-saved";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone?.Id.Should().Be("microphone-saved");
        fixture.ViewModel.SelectedOutputDevice?.Id.Should().Be("output-saved");
        fixture.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Selecting_System_clears_explicit_device_overrides()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("microphone-saved", "Saved microphone"),
            new MicrophoneDevice("microphone-default", "Windows microphone"),
        ];
        fixture.Voice.DefaultMicrophoneId = "microphone-default";
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-saved", "Saved speakers"),
            new AudioOutputDevice("output-default", "Windows speakers"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-default";
        fixture.AudioPreferences.MicrophoneId = "microphone-saved";
        fixture.AudioPreferences.OutputDeviceId = "output-saved";
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone = SystemAudioDevices.Microphone;
        fixture.ViewModel.SelectedOutputDevice = SystemAudioDevices.Output;

        fixture.AudioPreferences.MicrophoneId.Should().BeNull();
        fixture.AudioPreferences.OutputDeviceId.Should().BeNull();
        fixture.AudioPreferences.ClearedMicrophoneCount.Should().Be(1);
        fixture.AudioPreferences.ClearedOutputDeviceCount.Should().Be(1);
    }

    [Fact]
    public async Task Missing_device_overrides_require_explicit_replacement_without_overwriting_them()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("microphone-default", "Windows microphone"),
        ];
        fixture.Voice.DefaultMicrophoneId = "microphone-default";
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-default", "Windows speakers"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-default";
        fixture.AudioPreferences.MicrophoneId = "microphone-removed";
        fixture.AudioPreferences.OutputDeviceId = "output-removed";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone.Should().BeNull();
        fixture.ViewModel.SelectedOutputDevice.Should().BeNull();
        fixture.ViewModel.MicrophoneAvailabilityMessage.Should().StartWith("The saved microphone is no longer available.");
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().StartWith("The saved audio output device is no longer available.");
        fixture.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_reports_when_no_microphone_is_available()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.MicrophoneAvailabilityMessage.Should().Contain("no active default microphone");
        fixture.ViewModel.ResponseTitle.Should().Be("No microphone detected.");
        fixture.ViewModel.ListeningStatus.Should().Be("Microphone closed");
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.ListeningButtonText.Should().Be("Enable listening");
    }

    [Fact]
    public async Task InitializeAsync_selects_an_installed_female_voice_by_default()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("male", "Male voice", "en-US", SpeechVoiceGender.Male),
            new SpeechVoice("female", "Female voice", "en-US", SpeechVoiceGender.Female),
        ];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.Voices.Should().Equal(fixture.TextToSpeech.Voices);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
    }

    [Fact]
    public async Task InitializeAsync_selects_a_compatible_male_voice_when_no_female_voice_is_available()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("male", "Male voice", "en-US", SpeechVoiceGender.Male),
        ];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice?.Id.Should().Be("male");
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeTrue();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("Male voice");
    }

    [Fact]
    public async Task InitializeAsync_prefers_the_saved_voice_over_the_culture_default()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Female voice", "en-US", SpeechVoiceGender.Female),
            new SpeechVoice("saved", "Saved voice", "fr-FR", SpeechVoiceGender.Male),
        ];
        fixture.Preferences.VoiceId = "saved";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice?.Id.Should().Be("saved");
    }

    [Fact]
    public async Task InitializeAsync_reports_and_recovers_when_the_saved_voice_is_no_longer_installed()
    {
        var fixture = new Fixture();
        fixture.Preferences.VoiceId = "removed";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
        fixture.ViewModel.VoiceAvailabilityMessage.Should().StartWith("The saved voice is unavailable.");
    }

    [Fact]
    public async Task InitializeAsync_reports_when_no_speech_pack_is_installed()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.TextToSpeech.Voices = [];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("Speech output is unavailable.");
        fixture.ViewModel.ResponseBody.Should().Contain("Install a Windows text-to-speech voice");
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("No Windows speech pack is available");
    }

    [Fact]
    public async Task InitializeAsync_restores_the_saved_provider_and_its_voice()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
            CreateKokoroVoice("af_bella", "Bella"),
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        fixture.Preferences.VoiceId = "af_bella";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.Voices.Should().OnlyContain(
            voice => voice.ProviderId == SpeechProviderIds.Kokoro);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("af_bella");
        fixture.ViewModel.SpeechProviderAvailabilityMessage.Should().Contain("installed locally");
    }

    [Fact]
    public async Task InitializeAsync_falls_back_to_Windows_when_the_saved_provider_is_unknown()
    {
        var fixture = new Fixture();
        fixture.Preferences.ProviderId = "removed-provider";
        fixture.Preferences.VoiceId = "removed-voice";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(SpeechProviderIds.Windows);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
        fixture.ViewModel.VoiceAvailabilityMessage.Should().StartWith("Female voice");
    }

    [Fact]
    public async Task Refresh_preserves_the_active_provider_before_saved_provider_preferences()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Preferences.ProviderId = "removed-provider";
        fixture.Preferences.VoiceId = "removed-voice";

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(
            SpeechProviderIds.Windows);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
        fixture.ViewModel.VoiceAvailabilityMessage.Should().StartWith("Female voice");
    }

    [Fact]
    public async Task InitializeAsync_uses_the_only_provider_when_Windows_is_absent()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            CreateKokoroVoice("af_heart", "Heart"),
        ];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(
            SpeechProviderIds.Kokoro);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("af_heart");
    }

    [Fact]
    public async Task InitializeAsync_handles_an_empty_provider_catalog()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers = [];
        fixture.Preferences.VoiceId = "removed-voice";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedSpeechProvider.Should().BeNull();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.SpeechProviderAvailabilityMessage.Should().Be(
            "No speech provider is available.");
        fixture.ViewModel.SpeechProviderDownloadButtonText.Should().Be("Download");
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeFalse();
        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeFalse();
    }

    [Fact]
    public async Task Selecting_a_provider_filters_voices_and_saves_the_preference()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        fixture.Preferences.SavedProviderId.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.Voices.Should().ContainSingle()
            .Which.Id.Should().Be("af_heart");
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("af_heart");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.speech-provider",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Provider_without_a_declared_size_uses_a_generic_download_label()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var provider = new SpeechProvider(
            "optional",
            "Optional",
            "Optional provider.",
            IsInstalled: false,
            IsBuiltIn: false,
            DownloadSizeBytes: null,
            DefaultVoiceId: null);

        fixture.ViewModel.SelectedSpeechProvider = provider;

        fixture.ViewModel.SpeechProviderDownloadButtonText.Should().Be("Download");
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(PreferenceSaveFailures))]
    public async Task Selecting_a_provider_reports_preference_save_failures(
        Exception exception)
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.Preferences.ProviderSaveException = exception;

        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        fixture.ViewModel.ResponseTitle.Should().Be(
            "The speech provider preference could not be saved.");
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeFalse();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.speech-provider",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            exception is UnauthorizedAccessException ? "access-denied" : "io-error");
    }

    [Fact]
    public async Task Downloading_Kokoro_activates_it_without_a_restart()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        fixture.ViewModel.SpeechProviderDownloadButtonText.Should().Be(
            "Download (219 MB)");
        await fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync();

        fixture.TextToSpeech.InstallProviderCalls.Should().Be(1);
        fixture.ViewModel.SelectedSpeechProvider?.IsInstalled.Should().BeTrue();
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("af_heart");
        fixture.Preferences.SavedProviderId.Should().Be(SpeechProviderIds.Kokoro);
        fixture.Preferences.SavedVoiceId.Should().Be("af_heart");
        fixture.ViewModel.SpeechProviderOperationProgress.Should().Be(100);
        fixture.ViewModel.IsSpeechProviderOperationActive.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Kokoro is ready.");
        fixture.TextToSpeech.SpokenText.Should().Contain("Kokoro is ready.");
        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("af_heart");
        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeTrue();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "speech-provider.install",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Downloading_a_provider_without_voices_keeps_the_working_provider_active()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        await fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync();

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("female");
    }

    [Fact]
    public async Task Download_failure_uses_an_installed_non_Windows_provider()
    {
        const string localProviderId = "local";
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            new SpeechProvider(
                localProviderId,
                "Local",
                "Installed local provider.",
                IsInstalled: true,
                IsBuiltIn: false,
                DownloadSizeBytes: null,
                DefaultVoiceId: "local-voice"),
            CreateKokoroProvider(isInstalled: false),
        ];
        fixture.TextToSpeech.Voices = [];
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice(
                "local-voice",
                "Local voice",
                "en-US",
                SpeechVoiceGender.Female)
            {
                ProviderId = localProviderId,
            },
        ];
        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));
        fixture.TextToSpeech.InstallProviderException =
            new InvalidDataException("invalid");

        await fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync();

        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("local-voice");
    }

    [Fact]
    public void Selecting_a_voice_without_a_provider_does_not_activate_speech()
    {
        var fixture = new Fixture();

        fixture.ViewModel.SelectedVoice =
            new SpeechVoice("voice", "Voice", "en-US", SpeechVoiceGender.Female);

        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Provider_download_disables_provider_actions_while_active()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        fixture.TextToSpeech.Voices =
        [
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        fixture.TextToSpeech.InstallProviderGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        var downloadTask =
            fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync();
        await fixture.TextToSpeech.InstallProviderStarted.Task;

        fixture.ViewModel.IsSpeechProviderOperationActive.Should().BeTrue();
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeFalse();
        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeFalse();
        fixture.TextToSpeech.InstallProviderGate.SetResult();
        await downloadTask;
    }

    [Fact]
    public async Task Active_speech_disables_provider_removal()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.ViewModel.InitializeAsync();

        var previewTask = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;

        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeFalse();
        fixture.TextToSpeech.SpeakGate.SetResult();
        await previewTask;
        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeTrue();
    }

    [Fact]
    public void Unsupported_provider_progress_stage_is_rejected()
    {
        var fixture = new Fixture();
        var progress = new SpeechProviderInstallProgress(
            (SpeechProviderInstallStage)99,
            1,
            1);

        var action = () =>
            fixture.ViewModel.UpdateSpeechProviderInstallProgress(progress);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Installed_provider_without_a_compatible_default_explains_voice_selection()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true) with
            {
                DefaultVoiceId = null,
            },
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice(
                "foreign",
                "Foreign",
                "zh-CN",
                SpeechVoiceGender.Female)
            {
                ProviderId = SpeechProviderIds.Kokoro,
            },
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Be(
            "No voice matches the Windows profile culture. Choose another voice.");
    }

    [Theory]
    [MemberData(nameof(SpeechProviderInstallFailures))]
    public async Task Downloading_a_provider_surfaces_expected_failures(
        Exception exception,
        string expectedTitle,
        string expectedReason)
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));
        fixture.TextToSpeech.InstallProviderException = exception;

        await fixture.ViewModel.DownloadSpeechProviderCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.ViewModel.IsSpeechProviderOperationActive.Should().BeFalse();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().Contain(expectedTitle);
        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("female");
        fixture.Preferences.SavedProviderId.Should().BeNull();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "speech-provider.install",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            expectedReason);
    }

    [Fact]
    public async Task Removing_Kokoro_disables_its_voices_without_a_restart()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.RemoveSpeechProviderCommand.ExecuteAsync();

        fixture.TextToSpeech.RemoveProviderCalls.Should().Be(1);
        fixture.ViewModel.SelectedSpeechProvider?.IsInstalled.Should().BeFalse();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Kokoro was removed.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "speech-provider.remove",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [MemberData(nameof(SpeechProviderRemoveFailures))]
    public async Task Removing_a_provider_surfaces_expected_failures(
        Exception exception,
        string expectedReason)
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice("female", "Windows voice", "en-US", SpeechVoiceGender.Female),
            CreateKokoroVoice("af_heart", "Heart"),
        ];
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        fixture.TextToSpeech.RemoveProviderException = exception;
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.RemoveSpeechProviderCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be(
            "The speech provider could not be removed.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "speech-provider.remove",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            expectedReason);
    }

    public static TheoryData<Exception, string, string> SpeechProviderInstallFailures => new()
    {
        {
            new HttpRequestException("offline"),
            "The speech provider could not be downloaded.",
            "download-failed"
        },
        {
            new InvalidDataException("invalid"),
            "The speech provider download was invalid.",
            "validation-failed"
        },
        {
            new UnauthorizedAccessException("denied"),
            "The speech provider could not be installed.",
            "access-denied"
        },
        {
            new IOException("disk"),
            "The speech provider could not be installed.",
            "io-error"
        },
        {
            new InvalidOperationException("unsupported"),
            "The speech provider could not be prepared.",
            "provider-unavailable"
        },
    };

    public static TheoryData<Exception, string> SpeechProviderRemoveFailures => new()
    {
        { new UnauthorizedAccessException("denied"), "access-denied" },
        { new IOException("disk"), "io-error" },
        { new InvalidOperationException("busy"), "provider-busy" },
    };

    [Fact]
    public async Task InitializeAsync_reports_when_microphone_and_speech_pack_are_missing()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Voices = [];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Voice setup is incomplete.");
        fixture.ViewModel.ResponseBody.Should().Contain("install a Windows speech pack");
    }

    [Fact]
    public async Task InitializeAsync_reports_when_no_audio_output_device_is_available()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.TextToSpeech.OutputDevices = [];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is unavailable.");
        fixture.ViewModel.ResponseBody.Should().Contain("Connect or enable a Windows audio output device");
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("no active default audio output");
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task No_microphone_or_audio_output_reports_incomplete_voice_setup()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.OutputDevices = [];

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Voice setup is incomplete.");
        fixture.ViewModel.ResponseBody.Should().Contain("microphone and audio output");
    }

    [Fact]
    public async Task Muted_default_output_forces_visual_text_without_clearing_the_selection()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("muted", "Muted speakers", IsMuted: true),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "muted";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is muted.");
        fixture.ViewModel.ResponseBody.Should().Contain("Unmute the selected Windows audio output");
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Visual text is forced");
    }

    [Fact]
    public async Task Refresh_recovers_speech_after_the_selected_output_is_unmuted()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var outputDeviceId = fixture.TextToSpeech.DefaultOutputDeviceId!;
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice(outputDeviceId, "Default output", IsMuted: true),
        ];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice(outputDeviceId, "Default output"),
        ];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Environment check complete.");
    }

    [Fact]
    public async Task Unselected_audio_output_forces_visual_response_without_attempting_playback()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.SelectedOutputDevice = null;
        fixture.TextToSpeech.ClearSpokenResponse();

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Select an audio output device");
    }

    [Fact]
    public async Task Removed_audio_output_is_not_silently_replaced_during_refresh()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("replacement", "Replacement output"),
        ];

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedOutputDevice.Should().BeNull();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("no longer available");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
    }

    [Fact]
    public async Task Configured_response_mode_controls_effective_output_when_speech_is_available()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.ResponseOutputModes.Should().Equal(
            ResponseOutputMode.Hybrid,
            ResponseOutputMode.VoiceOnly,
            ResponseOutputMode.VisualOnly);
        fixture.ViewModel.ResponseModeOptions.Select(option => option.Label).Should().Equal(
            "Both audible and visual",
            "Audible only",
            "Visual only");
        fixture.ViewModel.DefaultResponseModeOption.Label.Should().Be(
            "Both audible and visual");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();

        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VoiceOnly;

        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();

        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VisualOnly;

        fixture.ViewModel.ConfiguredResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.DefaultResponseModeOption.Label.Should().Be("Visual only");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();

        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Speech_output_availability_requires_voice_and_unmuted_output()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var voice = fixture.ViewModel.SelectedVoice;

        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();

        fixture.ViewModel.SelectedVoice = null;
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.ViewModel.SelectedVoice = voice;
        fixture.ViewModel.SelectedOutputDevice = null;
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.ViewModel.SelectedOutputDevice =
            new AudioOutputDevice("muted", "Muted", IsMuted: true);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Listening_availability_supports_System_explicit_and_unselected_microphones()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeTrue();

        fixture.ViewModel.SelectedMicrophone = fixture.Voice.Microphones[0];
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeTrue();

        fixture.ViewModel.SelectedMicrophone = null;
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Task_and_queue_modes_override_the_persisted_device_default()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ResponseModeOverrideOptions.Select(option => option.Label).Should().Equal(
            "Inherit",
            "Both audible and visual",
            "Audible only",
            "Visual only");
        fixture.ViewModel.DefaultResponseModeOption =
            fixture.ViewModel.ResponseModeOptions.Single(
                option => option.Mode == ResponseOutputMode.VisualOnly);
        fixture.ViewModel.QueueResponseModeOption =
            fixture.ViewModel.ResponseModeOverrideOptions.Single(
                option => option.Mode == ResponseOutputMode.Hybrid);
        fixture.ViewModel.TaskResponseModeOption =
            fixture.ViewModel.ResponseModeOverrideOptions.Single(
                option => option.Mode == ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.QueueResponseModeOption = fixture.ViewModel.QueueResponseModeOption;
        fixture.ViewModel.TaskResponseModeOption = fixture.ViewModel.TaskResponseModeOption;

        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.ResponseOutputStatus.Should().StartWith("Current task override");
        fixture.ViewModel.QueueResponseModeOption.Label.Should().Be("Both audible and visual");
        fixture.ViewModel.TaskResponseModeOption.Label.Should().Be("Audible only");

        fixture.ViewModel.TaskResponseModeOption =
            fixture.ViewModel.ResponseModeOverrideOptions[0];

        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.Hybrid);
        fixture.ViewModel.ResponseOutputStatus.Should().StartWith("Current queue override");
        fixture.ViewModel.TaskResponseModeOption.Label.Should().Be("Inherit");

        fixture.ViewModel.QueueResponseModeOption =
            fixture.ViewModel.ResponseModeOverrideOptions[0];

        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.VisualOnly);
        fixture.ViewModel.ResponseOutputStatus.Should().StartWith("Device default");
        fixture.ViewModel.QueueResponseModeOption.Label.Should().Be("Inherit");
    }

    [Fact]
    public void Response_mode_override_dropdowns_reject_unavailable_options()
    {
        var fixture = new Fixture();

        var nullQueue = () => fixture.ViewModel.QueueResponseModeOption = null!;
        var unavailableTask = () => fixture.ViewModel.TaskResponseModeOption =
            new ResponseModeOverrideOption("Unavailable", ResponseOutputMode.Hybrid);

        nullQueue.Should().Throw<ArgumentNullException>();
        unavailableTask.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Device_response_mode_dropdown_rejects_unavailable_options()
    {
        var fixture = new Fixture();

        var unavailable = () => fixture.ViewModel.DefaultResponseModeOption =
            new ResponseModeOverrideOption(
                "Unavailable",
                ResponseOutputMode.Hybrid);

        unavailable.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Hybrid_and_voice_only_modes_speak_typed_responses()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().Contain("That isn't a supported built-in command.");

        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("Kora, what can you do");

        fixture.TextToSpeech.SpokenText.Should().Contain("Built-in commands are ready.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Visual_only_mode_does_not_speak_responses()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VisualOnly;

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Call_aware_settings_default_to_visual_override_with_voice_activation_enabled()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeTrue();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.ViewModel.CallVisualOverrideButtonText.Should().Be("Use normal response mode during calls");
        fixture.ViewModel.CallVisualOverrideStatus.Should().StartWith("On");
        fixture.ViewModel.CallVoiceActivationStatus.Should().StartWith("On");
        fixture.ViewModel.CurrentCallState.Should().Be(CallState.Unavailable);
        fixture.ViewModel.CallStateStatus.Should().Contain("unavailable");
    }

    [Fact]
    public async Task Detected_call_is_safe_without_a_window_action_subscriber()
    {
        var fixture = await Fixture.CreateInitializedAsync(subscribeToWindowActions: false);

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsCallVisualOverrideActive.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_loads_saved_call_aware_settings_without_resaving_them()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.CallPreferences.Settings = new CallAwareSettings(false, false);

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeFalse();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        fixture.CallPreferences.SavedSettings.Should().BeNull();
    }

    [Fact]
    public async Task Detected_call_uses_visual_only_responses_without_disabling_voice_activation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsCallDetected.Should().BeTrue();
        fixture.ViewModel.IsCallVisualOverrideActive.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeTrue();
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeTrue();
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Detected-call override");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Disabling_call_visual_override_restores_the_normal_response_mode()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;

        await fixture.ViewModel.ToggleCallVisualOverrideCommand.ExecuteAsync();
        fixture.CallState.SetState(CallState.Suspected);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeFalse();
        fixture.ViewModel.IsCallDetected.Should().BeTrue();
        fixture.ViewModel.IsCallVisualOverrideActive.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();
        fixture.ViewModel.CallVisualOverrideButtonText.Should().Be("Show visual text during calls");
        fixture.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(false, true));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.call-aware-policy",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().NotBeNull();
    }

    [Fact]
    public async Task Voice_activation_remains_listening_when_a_call_is_detected_by_default()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.Voice.StopCalls.Should().Be(0);
    }

    [Fact]
    public async Task Disabled_call_voice_activation_stops_listening_and_recovers_after_the_call()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ToggleCallVoiceActivationCommand.ExecuteAsync();

        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeFalse();
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.ListeningStatus.Should().Contain("paused during detected call");
        fixture.ViewModel.CallVoiceActivationButtonText.Should().Be("Keep voice activation during calls");
        fixture.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(true, false));

        fixture.CallState.SetState(CallState.Clear);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsVoiceActivationAvailable.Should().BeTrue();
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Call_detection_stops_existing_listening_when_voice_activation_was_disabled()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleCallVoiceActivationCommand.ExecuteAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.Voice.StopCalls.Should().Be(1);

        await fixture.ViewModel.ToggleCallVoiceActivationCommand.ExecuteAsync();

        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeTrue();
        fixture.ViewModel.CallVoiceActivationButtonText.Should().Be("Disable voice activation during calls");
    }

    [Fact]
    public async Task Enabling_visual_override_during_a_call_stops_current_speech()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleCallVisualOverrideCommand.ExecuteAsync();
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseTask = fixture.RunAsync("unsupported");
        await fixture.TextToSpeech.SpeakStarted.Task;

        await fixture.ViewModel.ToggleCallVisualOverrideCommand.ExecuteAsync();

        fixture.TextToSpeech.StopCalls.Should().Be(1);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsCallVisualOverrideActive.Should().BeTrue();

        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await responseTask;
    }

    [Theory]
    [InlineData(CallState.Active, "active")]
    [InlineData(CallState.Suspected, "suspected")]
    [InlineData(CallState.Clear, "No call")]
    [InlineData(CallState.Unknown, "unknown")]
    public async Task Call_state_status_describes_detector_observations(
        CallState state,
        string expectedStatus)
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.CallState.SetState(state);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.CallStateStatus.Should().Contain(expectedStatus);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public async Task Call_aware_preference_save_failure_is_visible(Type exceptionType)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.CallPreferences.SaveException = (Exception)Activator.CreateInstance(exceptionType)!;

        await fixture.ViewModel.ToggleCallVisualOverrideCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("The call-aware settings could not be saved.");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.call-aware-policy",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            exceptionType == typeof(UnauthorizedAccessException) ? "access-denied" : "io-error");
    }

    [Fact]
    public async Task Call_state_dispatch_failure_is_visible()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Dispatcher.InvokeException = new InvalidOperationException("dispatcher unavailable");

        fixture.CallState.SetState(CallState.Active);

        fixture.ViewModel.ResponseTitle.Should().Be("The call-aware output policy could not be applied.");
        fixture.ViewModel.ResponseBody.Should().Be("dispatcher unavailable");
    }

    [Fact]
    public async Task Typed_setting_mutations_raise_notifications_for_every_bound_settings_view()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        await fixture.ViewModel.SetShowVisualTextDuringCallsAsync(false);
        await fixture.ViewModel.SetAllowVoiceActivationDuringCallsAsync(false);

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeFalse();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        fixture.ViewModel.CallVisualOverrideStatus.Should().StartWith("Off");
        fixture.ViewModel.CallVoiceActivationStatus.Should().StartWith("Off");
        changedProperties.Should().Contain(nameof(MainViewModel.ShowVisualTextDuringCalls));
        changedProperties.Should().Contain(nameof(MainViewModel.CallVisualOverrideStatus));
        changedProperties.Should().Contain(nameof(MainViewModel.AllowVoiceActivationDuringCalls));
        changedProperties.Should().Contain(nameof(MainViewModel.CallVoiceActivationStatus));
        fixture.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(false, false));
    }

    [Fact]
    public async Task Open_settings_command_requests_the_shared_settings_window()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        var command = fixture.Catalog.GetCommands().Single(
            item => item.Action == BuiltInAction.OpenSettings);

        await fixture.ViewModel.ExecuteAsync(command);

        settingsRequests.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().Be("Settings");
        fixture.ViewModel.ResponseBody.Should().Contain("settings window");
    }

    [Fact]
    public async Task Open_microphone_privacy_settings_command_opens_Windows_settings()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.OpenMicrophonePrivacySettingsCommand.ExecuteAsync();

        fixture.Events.Should().Contain("process.open-microphone-settings");
    }

    [Fact]
    public async Task Open_microphone_privacy_settings_command_reports_failure()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Process.OpenMicrophoneSettingsException = new InvalidOperationException("Settings unavailable.");

        await fixture.ViewModel.OpenMicrophonePrivacySettingsCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Windows microphone settings could not be opened.");
        fixture.ViewModel.ResponseBody.Should().Be("Settings unavailable.");
    }

    [Fact]
    public void ShowSettings_is_safe_without_a_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        var action = fixture.ViewModel.ShowSettings;

        action.Should().NotThrow();
    }

    [Fact]
    public async Task Open_documentation_command_requests_the_shared_documentation_window()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var documentationRequests = 0;
        fixture.ViewModel.DocumentationRequested += (_, _) => documentationRequests++;
        var command = fixture.Catalog.GetCommands().Single(
            item => item.Action == BuiltInAction.OpenDocumentation);

        await fixture.ViewModel.ExecuteAsync(command);

        documentationRequests.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().Be("Documentation");
        fixture.ViewModel.ResponseBody.Should().Contain("user guide");
    }

    [Fact]
    public void ShowDocumentation_is_safe_without_a_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        var action = fixture.ViewModel.ShowDocumentation;

        action.Should().NotThrow();
    }

    [Fact]
    public async Task Active_microphone_allows_voice_only_playback()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.TextToSpeech.ClearSpokenResponse();

        await fixture.Voice.RaiseTranscriptAsync("Kora, what can you do", 0.9f);

        fixture.TextToSpeech.SpokenText.Should().Contain("Built-in commands are ready.");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Audible only");
    }

    [Theory]
    [MemberData(nameof(ResponsePlaybackFailures))]
    public async Task Response_playback_failure_forces_voice_only_output_to_the_ui(
        Exception exception,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = exception;

        await fixture.RunAsync("unsupported");

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
    }

    public static TheoryData<Exception, string> ResponsePlaybackFailures => new()
    {
        {
            new ArgumentOutOfRangeException("voice", "removed"),
            "The selected speech voice is unavailable."
        },
        {
            new InvalidOperationException("response playback failed"),
            "Text-to-speech is unavailable."
        },
    };

    [Fact]
    public async Task System_audio_output_failure_forces_visual_response_without_changing_the_selection()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            "endpoint failed");

        await fixture.RunAsync("unsupported");

        fixture.ViewModel.SelectedVoice.Should().NotBeNull();
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("The selected audio output is unavailable.");
    }

    [Fact]
    public async Task Runtime_mute_forces_visual_response_but_keeps_the_endpoint_selected()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted,
            "endpoint muted");

        await fixture.RunAsync("unsupported");

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is muted.");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
    }

    [Fact]
    public async Task Runtime_mute_marks_an_explicit_output_as_muted()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted,
            "endpoint muted");

        await fixture.RunAsync("unsupported");

        fixture.ViewModel.SelectedOutputDevice?.Id.Should().Be("0");
        fixture.ViewModel.SelectedOutputDevice?.IsMuted.Should().BeTrue();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Visual text is forced");
    }

    [Fact]
    public async Task InitializeAsync_loads_the_persisted_device_default()
    {
        var fixture = new Fixture();
        fixture.OutputPreferences.Mode = ResponseOutputMode.VoiceOnly;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.OutputPreferences.SavedMode.Should().BeNull();
    }

    [Fact]
    public void Changing_the_device_default_persists_it()
    {
        var fixture = new Fixture();

        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly;

        fixture.OutputPreferences.SavedMode.Should().Be(ResponseOutputMode.VisualOnly);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-output",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [MemberData(nameof(OutputModeSaveFailures))]
    public void Changing_the_device_default_reports_save_failures(Exception exception)
    {
        var fixture = new Fixture();
        fixture.OutputPreferences.SaveException = exception;

        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The default response mode could not be saved.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-output",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            exception is UnauthorizedAccessException ? "access-denied" : "io-error");
    }

    public static TheoryData<Exception> OutputModeSaveFailures => new()
    {
        new UnauthorizedAccessException("denied"),
        new IOException("unavailable"),
    };

    [Fact]
    public void Invalid_response_mode_values_are_rejected()
    {
        var fixture = new Fixture();

        var invalidDefault = () => fixture.ViewModel.DefaultResponseMode = (ResponseOutputMode)100;
        var invalidQueue = () => fixture.ViewModel.QueueResponseMode = (ResponseOutputMode)100;
        var invalidTask = () => fixture.ViewModel.TaskResponseMode = (ResponseOutputMode)100;

        invalidDefault.Should().Throw<ArgumentOutOfRangeException>();
        invalidQueue.Should().Throw<ArgumentOutOfRangeException>();
        invalidTask.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Missing_speech_pack_forces_voice_only_output_to_the_ui()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.Voices = [];

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Visual text is forced");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("Speech output is unavailable.");
    }

    [Fact]
    public async Task Selecting_a_voice_saves_the_device_local_preference()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var alternative = new SpeechVoice("alternative", "Alternative", "en-GB", SpeechVoiceGender.Male);
        fixture.ViewModel.Voices.Add(alternative);

        fixture.ViewModel.SelectedVoice = alternative;

        fixture.Preferences.SavedVoiceId.Should().Be("alternative");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.voice-selection",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [MemberData(nameof(PreferenceSaveFailures))]
    public async Task Selecting_a_voice_reports_preference_save_failures(Exception exception)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var alternative = new SpeechVoice("alternative", "Alternative", "en-GB", SpeechVoiceGender.Male);
        fixture.ViewModel.Voices.Add(alternative);
        fixture.Preferences.SaveException = exception;

        fixture.ViewModel.SelectedVoice = alternative;

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The speech voice preference could not be saved.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.ViewModel.SelectedVoice.Should().Be(alternative);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.voice-selection",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            exception is UnauthorizedAccessException ? "access-denied" : "io-error");
    }

    public static TheoryData<Exception> PreferenceSaveFailures => new()
    {
        new UnauthorizedAccessException("denied"),
        new IOException("unavailable"),
    };

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Typed_command_is_disabled_for_blank_input(string text)
    {
        var fixture = new Fixture();

        fixture.ViewModel.CommandText = text;

        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Microphone_override_is_persisted_and_preserved_on_refresh()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones =
        [
            new MicrophoneDevice("0", "Headset"),
            new MicrophoneDevice("1", "Webcam"),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedMicrophone = fixture.Voice.Microphones[1];

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedMicrophone?.Id.Should().Be("1");
        fixture.AudioPreferences.SavedMicrophoneId.Should().Be("1");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.microphone",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Microphone_metadata_refresh_and_unselection_do_not_create_an_override()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var selectedId = fixture.ViewModel.SelectedMicrophone!.Id;

        fixture.ViewModel.SelectedMicrophone =
            new MicrophoneDevice(selectedId, "Updated endpoint name");
        fixture.ViewModel.SelectedMicrophone = null;

        fixture.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(
            auditEvent => string.Equals(
                auditEvent.ActionId,
                "configuration.microphone",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Speaker_override_is_persisted_and_preserved_on_refresh()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.OutputDevices =
        [
            new AudioOutputDevice("output-default", "Windows speakers"),
            new AudioOutputDevice("output-headset", "Headset"),
        ];
        fixture.TextToSpeech.DefaultOutputDeviceId = "output-default";
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[1];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedOutputDevice?.Id.Should().Be("output-headset");
        fixture.AudioPreferences.SavedOutputDeviceId.Should().Be("output-headset");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.audio-output",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Speaker_metadata_refresh_and_unselection_do_not_create_an_override()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var selectedId = fixture.ViewModel.SelectedOutputDevice!.Id;

        fixture.ViewModel.SelectedOutputDevice =
            new AudioOutputDevice(
                selectedId,
                "Updated system selection",
                IsSystemDefault: true);
        fixture.ViewModel.SelectedOutputDevice = null;

        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(
            auditEvent => string.Equals(
                auditEvent.ActionId,
                "configuration.audio-output",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, typeof(UnauthorizedAccessException), "access-denied")]
    [InlineData(true, typeof(IOException), "io-error")]
    [InlineData(false, typeof(UnauthorizedAccessException), "access-denied")]
    [InlineData(false, typeof(IOException), "io-error")]
    public async Task Device_override_save_failures_are_visible_and_audited(
        bool isMicrophone,
        Type exceptionType,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        if (isMicrophone)
        {
            fixture.Voice.Microphones =
            [
                new MicrophoneDevice("microphone-alternative", "Alternative microphone"),
            ];
            fixture.ViewModel.Microphones.Add(fixture.Voice.Microphones[0]);
            fixture.AudioPreferences.MicrophoneSaveException = exception;
            fixture.ViewModel.SelectedMicrophone = fixture.Voice.Microphones[0];
        }
        else
        {
            var output = new AudioOutputDevice("output-alternative", "Alternative output");
            fixture.ViewModel.OutputDevices.Add(output);
            fixture.AudioPreferences.OutputDeviceSaveException = exception;
            fixture.ViewModel.SelectedOutputDevice = output;
        }

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(
            isMicrophone
                ? "The microphone preference could not be saved."
                : "The audio output preference could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            isMicrophone ? "configuration.microphone" : "configuration.audio-output",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
    }

    [Theory]
    [MemberData(nameof(RefreshFailures))]
    public async Task InitializeAsync_surfaces_expected_probe_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = new Fixture();
        fixture.Voice.GetMicrophonesException = exception;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    public static TheoryData<Exception, string> RefreshFailures => new()
    {
        { new InvalidDataException("invalid preference"), "A saved setting is invalid." },
        { new UnauthorizedAccessException("denied"), "Storage or microphone access was denied." },
        { new IOException("unavailable"), "Dependency probing failed." },
        { new AudioOutputDeviceUnavailableException("endpoint unavailable"), "Windows audio output is unavailable." },
        { new InvalidOperationException("speech unavailable"), "Speech services are unavailable." },
    };

    [Fact]
    public async Task Enabling_and_disabling_listening_opens_and_releases_the_selected_microphone()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.Voice.StartedMicrophone.Should().Be(fixture.ViewModel.SelectedMicrophone);
        var commandPhrases = fixture.Catalog.GetCommands().SelectMany(command => command.AllPhrases);
        fixture.Voice.StartedPhrases.Should().BeEquivalentTo(
            commandPhrases.SelectMany(phrase => new[] { phrase, $"Kora {phrase}" }));
        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.ViewModel.State.Should().Be(AssistantState.Listening);
        fixture.ViewModel.ListeningButtonText.Should().Be("Disable listening");

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.ListeningStatus.Should().Contain("disabled manually");
        fixture.ViewModel.ResponseTitle.Should().Be("Listening disabled.");
    }

    [Fact]
    public async Task Preview_voice_uses_the_selected_local_voice()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.TextToSpeech.SpokenText.Should().Be("Hello, I'm Kora.");
        fixture.TextToSpeech.SpokenVoice.Should().Be(fixture.ViewModel.SelectedVoice);
        fixture.TextToSpeech.SpokenOutputDevice.Should().Be(fixture.ViewModel.SelectedOutputDevice);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_voice_keeps_listening_active()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeTrue();

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(0);
        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().Be("Hello, I'm Kora.");
    }

    [Fact]
    public async Task Prefixed_voice_command_interrupts_active_preview()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var previewTask = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        await fixture.Voice.RaiseTranscriptAsync("Kora, open settings", 0.9f);
        await previewTask;

        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
        settingsRequests.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeTrue();
    }

    [Fact]
    public async Task Unprefixed_voice_command_and_rejection_do_not_interrupt_active_preview()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var previewTask = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        await fixture.Voice.RaiseTranscriptAsync("open settings", 0.9f);
        fixture.Voice.RaiseFailure("assistant audio");

        fixture.TextToSpeech.StopCalls.Should().Be(0);
        settingsRequests.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Command not recognized.");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await previewTask;
    }

    [Fact]
    public async Task Recognition_matching_active_speech_is_ignored_as_echo()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseTask = fixture.Voice.RaiseTranscriptAsync(
            "Kora, what can you do",
            0.9f);
        await fixture.TextToSpeech.SpeakStarted.Task;
        await fixture.Voice.RaiseTranscriptAsync(
            "Kora, open documentation",
            0.95f);

        fixture.TextToSpeech.StopCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await responseTask;
    }

    [Theory]
    [InlineData(typeof(ArgumentOutOfRangeException), "The selected speech voice is unavailable.")]
    [InlineData(typeof(AudioOutputDeviceUnavailableException), "The selected audio output is unavailable.")]
    [InlineData(typeof(InvalidOperationException), "Text-to-speech is unavailable.")]
    public async Task Preview_voice_surfaces_expected_playback_failures(
        Type exceptionType,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakException =
            (Exception)Activator.CreateInstance(exceptionType, "unavailable")!;

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.ResponseBody.Should().Contain("unavailable");
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Playback_failure_forces_voice_only_output_to_the_ui()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new InvalidOperationException("playback failed");

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("Text-to-speech is unavailable.");
    }

    [Fact]
    public async Task Forced_visual_fallback_is_safe_without_a_window_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        fixture.ViewModel.ConfiguredResponseMode = ResponseOutputMode.VoiceOnly;

        await fixture.RunAsync("unsupported");

        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");
    }

    [Fact]
    public async Task Stop_speaking_command_stops_active_playback()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("Kora, stop speaking");

        fixture.TextToSpeech.StopCalls.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
        fixture.ViewModel.ResponseBody.Should().Be("No speech playback is active.");
    }

    [Fact]
    public async Task Other_commands_are_disabled_while_microphone_start_is_in_progress()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var start = fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.RefreshCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();

        fixture.Voice.StartGate.SetResult();
        await start;
    }

    [Fact]
    public async Task Listening_command_is_disabled_while_readiness_refresh_is_in_progress()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.IsBusy.Should().BeTrue();
        fixture.ViewModel.ToggleListeningCommand.CanExecute(null).Should().BeFalse();

        var overlappingRefresh = fixture.ViewModel.DetectMicrophonesAsync();
        fixture.Probe.Gate.SetResult();
        await Task.WhenAll(refresh, overlappingRefresh);
    }

    [Theory]
    [MemberData(nameof(StartFailures))]
    public async Task Enabling_listening_surfaces_expected_start_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StartException = exception;

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    public static TheoryData<Exception, string> StartFailures => new()
    {
        { new ArgumentOutOfRangeException("microphone", "gone"), "The selected microphone is unavailable." },
        { new InvalidOperationException("recognizer missing"), "Windows speech recognition is unavailable." },
    };

    [Fact]
    public async Task Unsupported_typed_text_reports_help_visually_without_invoking_platform_actions()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "restart";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
    }

    [Theory]
    [InlineData("Kora, open settings", "Settings")]
    [InlineData("Kora, what can you do", "Built-in commands are ready.")]
    [InlineData("Kora, what version are you running", "Kora version")]
    [InlineData("Kora, what are you currently working on", "Voice is not active.")]
    [InlineData("Kora, cancel task", "Cancelled.")]
    [InlineData("Kora, stop speaking", "Speech is stopped.")]
    [InlineData("Kora, what power action is pending", "No power action is pending.")]
    public async Task Informational_commands_return_the_expected_response(string phrase, string title)
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = phrase;

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
    }

    [Fact]
    public async Task Status_command_reports_the_active_microphone_while_listening()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        await fixture.RunAsync("Kora, what are you currently working on");

        fixture.ViewModel.ResponseTitle.Should().Be("Waiting for your command.");
        fixture.ViewModel.ResponseBody.Should().Contain("System");
    }

    [Fact]
    public async Task Open_setup_refreshes_microphones_and_dependencies()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "First")];
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.Microphones = [new MicrophoneDevice("1", "Replacement")];

        await fixture.RunAsync("Kora, open setup");

        fixture.ViewModel.Microphones.Should().HaveCount(2);
        fixture.ViewModel.Microphones.Should().Contain(SystemAudioDevices.Microphone);
        fixture.ViewModel.Microphones.Should().ContainSingle(device => device.Name == "Replacement");
        fixture.ViewModel.ResponseTitle.Should().Be("Environment check complete.");
    }

    [Fact]
    public async Task DetectMicrophonesAsync_refreshes_the_device_list_for_the_tray()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("2", "Tray microphone")];

        await fixture.ViewModel.DetectMicrophonesAsync();

        fixture.ViewModel.Microphones.Should().HaveCount(2);
        fixture.ViewModel.Microphones.Should().Contain(SystemAudioDevices.Microphone);
        fixture.ViewModel.Microphones.Should().ContainSingle(device => device.Name == "Tray microphone");
    }

    [Fact]
    public async Task ExitAsync_releases_listening_before_closing_from_the_tray()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        await fixture.ViewModel.ExitAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "window.Close");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Show_command_requests_show_and_reports_success()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "Kora, show Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Success);
        fixture.ViewModel.StateLabel.Should().Be("COMPLETE");
    }

    [Fact]
    public async Task Repeating_a_command_that_keeps_the_same_state_is_supported()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("Kora, show Kora");
        await fixture.RunAsync("Kora, show Kora");

        fixture.WindowActions.Should().Equal(WindowAction.Show, WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Success);
    }

    [Fact]
    public async Task ShowApplication_restores_a_hidden_constellation()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, hide Kora");
        fixture.WindowActions.Clear();

        fixture.ViewModel.ShowApplication();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Information);
    }

    [Fact]
    public void Presence_interaction_requests_the_constellation_and_is_safe_without_a_subscriber()
    {
        var fixture = new Fixture();
        var unsubscribedFixture = new Fixture(subscribeToWindowActions: false);

        fixture.ViewModel.NotifyPresenceInteraction();
        unsubscribedFixture.ViewModel.NotifyPresenceInteraction();

        fixture.WindowActions.Should().ContainSingle()
            .Which.Should().Be(WindowAction.ShowPresence);
    }

    [Fact]
    public async Task Hide_command_uses_the_tray_recovery_path_without_requiring_listening()
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = "Kora, hide Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Hide);
        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
    }

    [Fact]
    public async Task Hide_command_hides_when_listening_is_active()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, hide Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Hide);
        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
        fixture.ViewModel.IsListening.Should().BeTrue();
    }

    [Fact]
    public async Task Exit_command_releases_audio_before_requesting_window_close()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, exit Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "window.Close");
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task Restart_command_releases_audio_before_starting_replacement_and_closing()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, restart Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "process.restart", "window.Close");
        fixture.ViewModel.IsListening.Should().BeFalse();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ApplicationExecution,
            "application.restart",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Restart_failure_is_visible_and_audited_without_closing()
    {
        var fixture = new Fixture();
        fixture.Process.RestartException = new InvalidOperationException("launch failed");

        await fixture.RunAsync("Kora, restart Kora");

        fixture.ViewModel.ResponseTitle.Should().Be("Kora could not restart.");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ApplicationExecution,
            "application.restart",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Failed,
            "restart-failed");
    }

    [Fact]
    public async Task Lock_command_releases_audio_before_requesting_session_lock()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, lock the machine";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.Events.Should().ContainInOrder("voice.stop", "session.lock");
        fixture.ViewModel.IsListening.Should().BeFalse();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Failed_lock_request_is_reported_without_reopening_audio()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Session.LockResult = false;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.CommandText = "Kora, lock the machine";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("Windows did not accept the lock request.");
        fixture.ViewModel.IsListening.Should().BeFalse();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Failed,
            "os-request-rejected");
    }

    [Fact]
    public async Task Lock_failure_is_visible_and_audited()
    {
        var fixture = new Fixture();
        fixture.Session.LockException = new InvalidOperationException("lock unavailable");

        await fixture.RunAsync("Kora, lock the machine");

        fixture.ViewModel.ResponseTitle.Should().Be("Windows could not lock the current session.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Failed,
            "lock-failed");
    }

    [Fact]
    public async Task Voice_lock_command_records_the_voice_initiator()
    {
        var fixture = new Fixture();

        await fixture.Voice.RaiseTranscriptAsync("Kora, lock the machine", 0.9f);

        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ProtectedOperation,
            "session.lock",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("Kora, shut down the computer", "Shutdown request recognized.", "power.shutdown")]
    [InlineData("Kora, restart the computer", "Restart request recognized.", "power.restart")]
    public async Task Power_commands_create_only_a_non_destructive_proposal(
        string phrase,
        string title,
        string actionId)
    {
        var fixture = new Fixture();
        fixture.ViewModel.CommandText = phrase;

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Waiting);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        var audit = fixture.Audit.Events.Should().ContainSingle().Subject;
        audit.Category.Should().Be(SecurityAuditCategory.SecurityApproval);
        audit.ActionId.Should().Be(actionId);
        audit.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audit.Initiator.Should().Be(SecurityAuditInitiator.TypedCommand);
        audit.TargetId.Should().Be("machine.current");
        audit.ApprovalId.Should().NotBeNull();
    }

    [Fact]
    public async Task Power_proposal_can_be_inspected_and_cancelled()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, restart the computer");

        await fixture.RunAsync("Kora, what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("Computer restart proposal pending.");

        await fixture.RunAsync("Kora, cancel computer restart");
        fixture.ViewModel.ResponseTitle.Should().Be("Power proposal cancelled.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.SecurityApproval,
            "power.restart",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Cancelled,
            "user-cancelled");
        fixture.Audit.Events[0].ApprovalId.Should().NotBeNull();
        fixture.Audit.Events[1].ApprovalId.Should().Be(fixture.Audit.Events[0].ApprovalId);

        await fixture.RunAsync("Kora, what power action is pending");
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
    }

    [Fact]
    public async Task Shutdown_proposal_can_be_inspected()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, shut down the computer");

        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.ResponseTitle.Should().Be("Shutdown proposal pending.");
    }

    [Fact]
    public async Task Replacing_a_power_proposal_audits_the_superseded_approval()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, shut down the computer");

        await fixture.RunAsync("Kora, restart the computer");

        fixture.Audit.Events.Should().HaveCount(3);
        fixture.Audit.Events[0].ActionId.Should().Be("power.shutdown");
        fixture.Audit.Events[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        fixture.Audit.Events[1].CorrelationId.Should().Be(fixture.Audit.Events[0].CorrelationId);
        fixture.Audit.Events[1].Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        fixture.Audit.Events[1].ReasonCode.Should().Be("superseded");
        fixture.Audit.Events[2].ActionId.Should().Be("power.restart");
        fixture.Audit.Events[2].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        fixture.Audit.Events[2].CorrelationId.Should().NotBe(fixture.Audit.Events[0].CorrelationId);
    }

    [Fact]
    public async Task Cancelling_a_task_audits_the_pending_power_approval()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, shut down the computer");

        await fixture.RunAsync("Kora, cancel task");

        AssertAuditPair(
            fixture,
            SecurityAuditCategory.SecurityApproval,
            "power.shutdown",
            SecurityAuditInitiator.TypedCommand,
            SecurityAuditOutcome.Cancelled,
            "task-cancelled");
    }

    [Fact]
    public async Task Cancelling_power_with_no_proposal_reports_that_nothing_is_pending()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("Kora, cancel shutdown");

        fixture.ViewModel.ResponseTitle.Should().Be("No Kora power action is pending.");
    }

    [Fact]
    public async Task Recognized_voice_transcript_uses_the_same_command_pipeline()
    {
        var fixture = new Fixture();

        await fixture.Voice.RaiseTranscriptAsync("Kora, show Kora", 0.87f);

        fixture.ViewModel.Transcript.Should().Contain(0.87f.ToString("P0", CultureInfo.CurrentCulture));
        fixture.WindowActions.Should().Equal(WindowAction.ShowPresence, WindowAction.Show);
    }

    [Fact]
    public async Task Recognized_voice_transcript_is_safe_without_a_window_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        await fixture.Voice.RaiseTranscriptAsync("Kora, what can you do", 0.87f);

        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Fact]
    public void Recognition_failure_is_dispatched_to_the_information_surface()
    {
        var fixture = new Fixture();

        fixture.Voice.RaiseFailure("not recognized");

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("Command not recognized.");
        fixture.ViewModel.ResponseBody.Should().Be("not recognized");
    }

    [Fact]
    public void Voice_dispatch_failure_is_reported()
    {
        var fixture = new Fixture();
        fixture.Dispatcher.InvokeException = new InvalidOperationException("dispatch failed");

        fixture.Voice.RaiseTranscript("Kora, show Kora", 0.9f);

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The command could not be completed.");
        fixture.ViewModel.ResponseBody.Should().Be("dispatch failed");
    }

    [Fact]
    public async Task ExecuteAsync_rejects_an_unknown_registered_action()
    {
        var fixture = new Fixture();
        var command = new CommandDefinition(
            (BuiltInAction)int.MaxValue,
            "invalid",
            "invalid",
            []);

        var action = () => fixture.ViewModel.ExecuteAsync(command);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported built-in action*");
    }

    [Fact]
    public async Task Show_command_is_safe_without_a_window_action_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        await fixture.RunAsync("Kora, show Kora");

        fixture.ViewModel.State.Should().Be(AssistantState.Success);
    }

    [Theory]
    [InlineData(BuiltInAction.ExitApplication)]
    [InlineData(BuiltInAction.RestartApplication)]
    public async Task Lifecycle_commands_are_safe_without_a_window_action_subscriber(BuiltInAction action)
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        var command = fixture.Catalog.GetCommands().Single(item => item.Action == action);

        await fixture.ViewModel.ExecuteAsync(command);

        fixture.Voice.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task Hide_is_safe_without_a_window_action_subscriber()
    {
        var fixture = await Fixture.CreateInitializedAsync(subscribeToWindowActions: false);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        var command = fixture.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.HideApplication);

        await fixture.ViewModel.ExecuteAsync(command);

        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
    }

    private static void AssertAuditPair(
        Fixture fixture,
        SecurityAuditCategory category,
        string actionId,
        SecurityAuditInitiator initiator,
        SecurityAuditOutcome finalOutcome,
        string? reasonCode = null)
    {
        var events = fixture.Audit.Events
            .Where(auditEvent => string.Equals(
                auditEvent.ActionId,
                actionId,
                StringComparison.Ordinal))
            .ToArray();
        events.Should().HaveCount(2);
        var requested = events[0];
        var completed = events[1];
        requested.CorrelationId.Should().NotBeEmpty();
        completed.CorrelationId.Should().Be(requested.CorrelationId);
        requested.Category.Should().Be(category);
        completed.Category.Should().Be(category);
        requested.ActionId.Should().Be(actionId);
        completed.ActionId.Should().Be(actionId);
        requested.Initiator.Should().Be(initiator);
        completed.Initiator.Should().Be(initiator);
        requested.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        requested.ReasonCode.Should().BeNull();
        completed.Outcome.Should().Be(finalOutcome);
        completed.ReasonCode.Should().Be(reasonCode);
    }

    private static bool SetConstellationSetting(
        MainViewModel viewModel,
        ConstellationSetting setting,
        int value,
        SecurityAuditInitiator initiator) =>
        setting switch
        {
            ConstellationSetting.Size =>
                viewModel.SetConstellationSizePixels(value, initiator),
            ConstellationSetting.DotSize =>
                viewModel.SetConstellationDotSizePercent(value, initiator),
            ConstellationSetting.MovementSpeed =>
                viewModel.SetConstellationMovementSpeedPercent(value, initiator),
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

    private static int GetConstellationSetting(
        MainViewModel viewModel,
        ConstellationSetting setting) =>
        setting switch
        {
            ConstellationSetting.Size => viewModel.ConstellationSizePixels,
            ConstellationSetting.DotSize => viewModel.ConstellationDotSizePercent,
            ConstellationSetting.MovementSpeed => viewModel.ConstellationMovementSpeedPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

    private static int? GetSavedConstellationSetting(
        FakeAppearancePreferences preferences,
        ConstellationSetting setting) =>
        setting switch
        {
            ConstellationSetting.Size => preferences.SavedConstellationSizePixels,
            ConstellationSetting.DotSize => preferences.SavedConstellationDotSizePercent,
            ConstellationSetting.MovementSpeed =>
                preferences.SavedConstellationMovementSpeedPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

    private static SpeechProvider CreateWindowsProvider() =>
        new(
            SpeechProviderIds.Windows,
            "Windows",
            "Windows voices.",
            IsInstalled: true,
            IsBuiltIn: true,
            DownloadSizeBytes: null,
            DefaultVoiceId: "female");

    private static SpeechProvider CreateKokoroProvider(bool isInstalled) =>
        new(
            SpeechProviderIds.Kokoro,
            "Kokoro",
            "Local neural speech.",
            isInstalled,
            IsBuiltIn: false,
            DownloadSizeBytes: 229_449_998,
            DefaultVoiceId: "af_heart");

    private static SpeechVoice CreateKokoroVoice(string id, string name) =>
        new(id, name, "en-US", SpeechVoiceGender.Female)
        {
            ProviderId = SpeechProviderIds.Kokoro,
        };

    public enum ConstellationSetting
    {
        Size,
        DotSize,
        MovementSpeed,
    }

    private sealed class Fixture
    {
        public Fixture(bool subscribeToWindowActions = true)
        {
            Catalog = new BuiltInCommandCatalog();
            Dispatcher = new ImmediateDispatcher();
            Voice = new FakeVoiceRecognitionService(Dispatcher, Events);
            TextToSpeech = new FakeTextToSpeechService(Events);
            NamePreferences = new FakeAssistantNamePreferences();
            AppearancePreferences = new FakeAppearancePreferences();
            MicrophoneAccess = new FakeMicrophoneAccessService();
            Preferences = new FakeTextToSpeechPreferences();
            AudioPreferences = new FakeAudioDevicePreferences();
            OutputPreferences = new FakeResponseOutputPreferences();
            CallPreferences = new FakeCallAwarePreferences();
            CallState = new FakeCallStateService();
            Session = new FakeSessionController(Events);
            Process = new FakeApplicationProcessController(Events);
            Audit = new FakeSecurityAuditLog();
            Probe = new StubProbe(new DependencyStatus(
                "storage",
                "Storage",
                DependencyReadiness.Ready,
                "ready"));
            var bootstrapper = new DependencyBootstrapper(
                [Probe],
                NullLogger<DependencyBootstrapper>.Instance);
            ViewModel = new MainViewModel(
                Catalog,
                new BuiltInCommandRouter(Catalog),
                bootstrapper,
                MicrophoneAccess,
                Voice,
                TextToSpeech,
                NamePreferences,
                AppearancePreferences,
                Preferences,
                AudioPreferences,
                OutputPreferences,
                CallPreferences,
                CallState,
                Session,
                Process,
                Dispatcher,
                new FakeApplicationInfo(),
                Audit,
                NullLogger<MainViewModel>.Instance);
            if (subscribeToWindowActions)
            {
                ViewModel.WindowActionRequested += (_, action) =>
                {
                    WindowActions.Add(action);
                    Events.Add($"window.{action}");
                };
            }
        }

        public BuiltInCommandCatalog Catalog { get; }

        public ImmediateDispatcher Dispatcher { get; }

        public FakeVoiceRecognitionService Voice { get; }

        public FakeTextToSpeechService TextToSpeech { get; }

        public FakeAssistantNamePreferences NamePreferences { get; }

        public FakeAppearancePreferences AppearancePreferences { get; }

        public FakeMicrophoneAccessService MicrophoneAccess { get; }

        public FakeTextToSpeechPreferences Preferences { get; }

        public FakeAudioDevicePreferences AudioPreferences { get; }

        public FakeResponseOutputPreferences OutputPreferences { get; }

        public FakeCallAwarePreferences CallPreferences { get; }

        public FakeCallStateService CallState { get; }

        public FakeSessionController Session { get; }

        public FakeApplicationProcessController Process { get; }

        public FakeSecurityAuditLog Audit { get; }

        public StubProbe Probe { get; }

        public MainViewModel ViewModel { get; }

        public List<WindowAction> WindowActions { get; } = [];

        public List<string> Events { get; } = [];

        public static async Task<Fixture> CreateInitializedAsync(bool subscribeToWindowActions = true)
        {
            var fixture = new Fixture(subscribeToWindowActions);
            fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
            fixture.Voice.DefaultMicrophoneId = "0";
            await fixture.ViewModel.InitializeAsync();
            await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
            await fixture.ViewModel.RefreshCommand.ExecuteAsync();
            fixture.Voice.ResetInteractionState();
            fixture.WindowActions.Clear();
            fixture.Events.Clear();
            return fixture;
        }

        public async Task RunAsync(string command)
        {
            ViewModel.CommandText = command;
            await ViewModel.RunTypedCommand.ExecuteAsync();
        }
    }

    private sealed class StubProbe(DependencyStatus status) : IDependencyProbe
    {
        public TaskCompletionSource? Gate { get; set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return status;
        }
    }

    private sealed class FakeMicrophoneAccessService : IMicrophoneAccessService
    {
        public MicrophoneAccessStatus Status { get; set; } = new(
            MicrophoneAccessState.Allowed,
            "Windows microphone access is allowed for desktop apps.");

        public MicrophoneAccessStatus GetStatus() => Status;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task LastInvocation { get; private set; } = Task.CompletedTask;

        public InvalidOperationException? InvokeException { get; set; }

        public Task InvokeAsync(Func<Task> action)
        {
            if (InvokeException is not null)
            {
                throw InvokeException;
            }

            LastInvocation = action();
            return LastInvocation;
        }

        public void Post(Action action) => action();
    }

    private sealed class FakeVoiceRecognitionService(
        ImmediateDispatcher dispatcher,
        List<string> events) : IVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

        public bool IsListening { get; private set; }

        public IReadOnlyList<MicrophoneDevice> Microphones { get; set; } = [];

        public Exception? GetMicrophonesException { get; set; }

        public Exception? StartException { get; set; }

        public TaskCompletionSource? StartGate { get; set; }

        public MicrophoneDevice? StartedMicrophone { get; private set; }

        public IReadOnlyList<string> StartedPhrases { get; private set; } = [];

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public void ResetInteractionState()
        {
            StartedMicrophone = null;
            StartedPhrases = [];
            StartCalls = 0;
            StopCalls = 0;
        }

        public IReadOnlyList<MicrophoneDevice> GetMicrophones()
        {
            if (GetMicrophonesException is not null)
            {
                throw GetMicrophonesException;
            }

            return Microphones;
        }

        public MicrophoneDevice? GetDefaultMicrophone() =>
            Microphones.FirstOrDefault(device => string.Equals(
                device.Id,
                DefaultMicrophoneId,
                StringComparison.Ordinal));

        public string? DefaultMicrophoneId { get; set; }

        public async Task StartAsync(
            MicrophoneDevice microphone,
            IEnumerable<string> phrases,
            CancellationToken cancellationToken = default)
        {
            StartedMicrophone = microphone;
            StartedPhrases = phrases.ToArray();
            StartCalls++;
            if (StartException is not null)
            {
                throw StartException;
            }

            if (StartGate is not null)
            {
                await StartGate.Task.WaitAsync(cancellationToken);
            }

            IsListening = true;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("voice.stop");
            StopCalls++;
            IsListening = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task RaiseTranscriptAsync(string transcript, float confidence)
        {
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence));
            await dispatcher.LastInvocation;
        }

        public void RaiseTranscript(string transcript, float confidence) =>
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence));

        public void RaiseFailure(string message) =>
            RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs(message));
    }

    private sealed class FakeTextToSpeechService(List<string> events) : ITextToSpeechService
    {
        public bool IsSpeaking { get; private set; }

        public IReadOnlyList<SpeechProvider> Providers { get; set; } =
        [
            new SpeechProvider(
                SpeechProviderIds.Windows,
                "Windows",
                "Windows voices.",
                IsInstalled: true,
                IsBuiltIn: true,
                DownloadSizeBytes: null,
                DefaultVoiceId: "female"),
        ];

        public IReadOnlyList<SpeechVoice> Voices { get; set; } =
        [
            new SpeechVoice("female", "Female voice", "en-US", SpeechVoiceGender.Female),
        ];

        public IReadOnlyList<AudioOutputDevice> OutputDevices { get; set; } =
        [
            new AudioOutputDevice("0", "Default output"),
        ];

        public string? DefaultOutputDeviceId { get; set; } = "0";

        public string? SpokenText { get; private set; }

        public SpeechVoice? SpokenVoice { get; private set; }

        public AudioOutputDevice? SpokenOutputDevice { get; private set; }

        public Exception? SpeakException { get; set; }

        public TaskCompletionSource? SpeakGate { get; set; }

        public TaskCompletionSource SpeakStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCalls { get; private set; }

        public int InstallProviderCalls { get; private set; }

        public int RemoveProviderCalls { get; private set; }

        public Exception? InstallProviderException { get; set; }

        public Exception? RemoveProviderException { get; set; }

        public TaskCompletionSource? InstallProviderGate { get; set; }

        public TaskCompletionSource InstallProviderStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<SpeechProviderInstallStage> InstallProgressStages { get; set; } =
        [
            SpeechProviderInstallStage.Downloading,
            SpeechProviderInstallStage.Verifying,
            SpeechProviderInstallStage.Extracting,
            SpeechProviderInstallStage.Preparing,
        ];

        public IReadOnlyList<SpeechProvider> GetProviders() => Providers;

        public IReadOnlyList<SpeechVoice> GetVoices() => Voices;

        public SpeechVoice? GetDefaultVoice() =>
            Voices.FirstOrDefault(voice => voice.Gender == SpeechVoiceGender.Female)
            ?? Voices.FirstOrDefault(voice => voice.Gender == SpeechVoiceGender.Male);

        public async Task SpeakAsync(
            string text,
            SpeechVoice voice,
            AudioOutputDevice outputDevice,
            CancellationToken cancellationToken = default)
        {
            if (SpeakException is not null)
            {
                throw SpeakException;
            }

            SpokenText = text;
            SpokenVoice = voice;
            SpokenOutputDevice = outputDevice;
            IsSpeaking = true;
            SpeakStarted.TrySetResult();
            if (SpeakGate is not null)
            {
                await SpeakGate.Task.WaitAsync(cancellationToken);
            }

            IsSpeaking = false;
        }

        public async Task InstallProviderAsync(
            string providerId,
            IProgress<SpeechProviderInstallProgress> progress,
            CancellationToken cancellationToken = default)
        {
            InstallProviderCalls++;
            if (InstallProviderException is not null)
            {
                throw InstallProviderException;
            }

            InstallProviderStarted.TrySetResult();
            foreach (var stage in InstallProgressStages)
            {
                progress.Report(new SpeechProviderInstallProgress(
                    stage,
                    stage == SpeechProviderInstallStage.Downloading ? 50 : 100,
                    100));
            }

            if (InstallProviderGate is not null)
            {
                await InstallProviderGate.Task.WaitAsync(cancellationToken);
            }

            Providers = Providers
                .Select(provider => string.Equals(
                        provider.Id,
                        providerId,
                        StringComparison.Ordinal)
                    ? provider with { IsInstalled = true }
                    : provider)
                .ToArray();
        }

        public Task RemoveProviderAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            RemoveProviderCalls++;
            if (RemoveProviderException is not null)
            {
                throw RemoveProviderException;
            }

            Providers = Providers
                .Select(provider => string.Equals(
                        provider.Id,
                        providerId,
                        StringComparison.Ordinal)
                    ? provider with { IsInstalled = false }
                    : provider)
                .ToArray();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("tts.stop");
            StopCalls++;
            IsSpeaking = false;
            SpeakGate?.TrySetResult();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void ClearSpokenResponse()
        {
            SpokenText = null;
            SpokenVoice = null;
            SpokenOutputDevice = null;
        }

        public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
            => OutputDevices;

        public AudioOutputDevice? GetDefaultOutputDevice() =>
            OutputDevices.FirstOrDefault(device => string.Equals(
                device.Id,
                DefaultOutputDeviceId,
                StringComparison.Ordinal));
    }

    private sealed class FakeAssistantNamePreferences : IAssistantNamePreferences
    {
        public string? Name { get; set; }

        public string? SavedName { get; private set; }

        public Exception? SaveException { get; set; }

        public string? LoadName() => Name;

        public void SaveName(string name)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedName = name;
            Name = name;
        }
    }

    private sealed class FakeAppearancePreferences : IAppearancePreferences
    {
        public ApplicationThemeMode? Mode { get; set; }

        public int? PresenceTimeoutSeconds { get; set; }

        public int? ConstellationSizePixels { get; set; }

        public int? ConstellationDotSizePercent { get; set; }

        public int? ConstellationMovementSpeedPercent { get; set; }

        public ConstellationPosition? ConstellationPosition { get; set; }

        public ResponseWindowSettings? ResponseWindowSettings { get; set; }

        public ApplicationThemeMode? SavedMode { get; private set; }

        public int? SavedPresenceTimeoutSeconds { get; private set; }

        public int? SavedConstellationSizePixels { get; private set; }

        public int? SavedConstellationDotSizePercent { get; private set; }

        public int? SavedConstellationMovementSpeedPercent { get; private set; }

        public ConstellationPosition? SavedConstellationPosition { get; private set; }

        public ResponseWindowSettings? SavedResponseWindowSettings { get; private set; }

        public Exception? LoadException { get; set; }

        public Exception? SaveException { get; set; }

        public ApplicationThemeMode? LoadThemeMode()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return Mode;
        }

        public void SaveThemeMode(ApplicationThemeMode mode)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedMode = mode;
            Mode = mode;
        }

        public int? LoadPresenceTimeoutSeconds()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceTimeoutSeconds;
        }

        public void SavePresenceTimeoutSeconds(int seconds)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedPresenceTimeoutSeconds = seconds;
            PresenceTimeoutSeconds = seconds;
        }

        public int? LoadConstellationSizePixels()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ConstellationSizePixels;
        }

        public int? LoadConstellationDotSizePercent()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ConstellationDotSizePercent;
        }

        public int? LoadConstellationMovementSpeedPercent()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ConstellationMovementSpeedPercent;
        }

        public ConstellationPosition? LoadConstellationPosition()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ConstellationPosition;
        }

        public ResponseWindowSettings? LoadResponseWindowSettings()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ResponseWindowSettings;
        }

        public void SaveConstellationSizePixels(int value)
        {
            ThrowIfSaveFails();
            SavedConstellationSizePixels = value;
            ConstellationSizePixels = value;
        }

        public void SaveConstellationDotSizePercent(int value)
        {
            ThrowIfSaveFails();
            SavedConstellationDotSizePercent = value;
            ConstellationDotSizePercent = value;
        }

        public void SaveConstellationMovementSpeedPercent(int value)
        {
            ThrowIfSaveFails();
            SavedConstellationMovementSpeedPercent = value;
            ConstellationMovementSpeedPercent = value;
        }

        public void SaveConstellationPosition(ConstellationPosition position)
        {
            ThrowIfSaveFails();
            SavedConstellationPosition = position;
            ConstellationPosition = position;
        }

        public void SaveResponseWindowSettings(ResponseWindowSettings settings)
        {
            ThrowIfSaveFails();
            SavedResponseWindowSettings = settings;
            ResponseWindowSettings = settings;
        }

        private void ThrowIfSaveFails()
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }
        }
    }

    private sealed class FakeTextToSpeechPreferences : ITextToSpeechPreferences
    {
        public string? ProviderId { get; set; }

        public string? VoiceId { get; set; }

        public string? SavedProviderId { get; private set; }

        public string? SavedVoiceId { get; private set; }

        public Exception? SaveException { get; set; }

        public Exception? ProviderSaveException { get; set; }

        public string? LoadProviderId() => ProviderId;

        public void SaveProviderId(string providerId)
        {
            if (ProviderSaveException is not null)
            {
                throw ProviderSaveException;
            }

            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedProviderId = providerId;
        }

        public string? LoadVoiceId() => VoiceId;

        public void SaveVoiceId(string voiceId)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedVoiceId = voiceId;
        }
    }

    private sealed class FakeAudioDevicePreferences : IAudioDevicePreferences
    {
        public string? MicrophoneId { get; set; }

        public string? OutputDeviceId { get; set; }

        public string? SavedMicrophoneId { get; private set; }

        public string? SavedOutputDeviceId { get; private set; }

        public int ClearedMicrophoneCount { get; private set; }

        public int ClearedOutputDeviceCount { get; private set; }

        public Exception? MicrophoneSaveException { get; set; }

        public Exception? OutputDeviceSaveException { get; set; }

        public string? LoadMicrophoneId() => MicrophoneId;

        public string? LoadOutputDeviceId() => OutputDeviceId;

        public void SaveMicrophoneId(string microphoneId)
        {
            if (MicrophoneSaveException is not null)
            {
                throw MicrophoneSaveException;
            }

            SavedMicrophoneId = microphoneId;
            MicrophoneId = microphoneId;
        }

        public void SaveOutputDeviceId(string outputDeviceId)
        {
            if (OutputDeviceSaveException is not null)
            {
                throw OutputDeviceSaveException;
            }

            SavedOutputDeviceId = outputDeviceId;
            OutputDeviceId = outputDeviceId;
        }

        public void ClearMicrophoneId()
        {
            if (MicrophoneSaveException is not null)
            {
                throw MicrophoneSaveException;
            }

            ClearedMicrophoneCount++;
            MicrophoneId = null;
        }

        public void ClearOutputDeviceId()
        {
            if (OutputDeviceSaveException is not null)
            {
                throw OutputDeviceSaveException;
            }

            ClearedOutputDeviceCount++;
            OutputDeviceId = null;
        }
    }

    private sealed class FakeResponseOutputPreferences : IResponseOutputPreferences
    {
        public ResponseOutputMode? Mode { get; set; }

        public ResponseOutputMode? SavedMode { get; private set; }

        public Exception? SaveException { get; set; }

        public ResponseOutputMode? LoadDefaultMode() => Mode;

        public void SaveDefaultMode(ResponseOutputMode mode)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedMode = mode;
            Mode = mode;
        }
    }

    private sealed class FakeCallAwarePreferences : ICallAwarePreferences
    {
        public CallAwareSettings? Settings { get; set; }

        public CallAwareSettings? SavedSettings { get; private set; }

        public Exception? SaveException { get; set; }

        public CallAwareSettings? Load() => Settings;

        public void Save(CallAwareSettings settings)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedSettings = settings;
            Settings = settings;
        }
    }

    private sealed class FakeCallStateService : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;

        public CallState CurrentState { get; private set; } = CallState.Unavailable;

        public void SetState(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new CallStateChangedEventArgs(state));
        }
    }

    private sealed class FakeSessionController(List<string> events) : ISessionController
    {
        public InvalidOperationException? LockException { get; set; }

        public bool IsUnlocked { get; set; } = true;

        public bool LockResult { get; set; } = true;

        public int LockCalls { get; private set; }

        public bool IsCurrentSessionUnlocked() => IsUnlocked;

        public bool LockCurrentSession()
        {
            events.Add("session.lock");
            if (LockException is not null)
            {
                throw LockException;
            }

            LockCalls++;
            return LockResult;
        }
    }

    private sealed class FakeApplicationProcessController(
        List<string> events) : IApplicationProcessController
    {
        public InvalidOperationException? RestartException { get; set; }

        public InvalidOperationException? OpenMicrophoneSettingsException { get; set; }

        public void OpenWindowsMicrophonePrivacySettings()
        {
            events.Add("process.open-microphone-settings");
            if (OpenMicrophoneSettingsException is not null)
            {
                throw OpenMicrophoneSettingsException;
            }
        }

        public void RestartCurrentApplication()
        {
            events.Add("process.restart");
            if (RestartException is not null)
            {
                throw RestartException;
            }
        }
    }

    private sealed class FakeSecurityAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    private sealed class FakeApplicationInfo : IApplicationInfo
    {
        public string Version => "1.2.3";
    }
}