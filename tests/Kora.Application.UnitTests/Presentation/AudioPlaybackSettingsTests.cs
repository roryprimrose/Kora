using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Configuration;
using Kora.Core.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Presentation;

public sealed class AudioPlaybackSettingsTests
{
    [Fact]
    public async Task Qualified_settings_initialize_defaults_and_keep_pending_selection_separate_from_saved_state()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var settings = fixture.CreateSettings();
        settings.SelectedPlaybackVolume.Should().Be(100);
        settings.SelectedWindowsSpeechRate.Should().Be(0);
        settings.VolumeState!.Available.Should().BeTrue();
        settings.RateState!.Available.Should().BeTrue();
        settings.PlaybackVolumeChoices.Should().Equal(Enumerable.Range(0, 101));
        settings.WindowsSpeechRateChoices.Should().Equal(Enumerable.Range(-10, 21));
        settings.PlaybackVolumeStatus.Should().Contain("Using the default");
        settings.WindowsSpeechRateStatus.Should().Contain("Using the default");
        settings.SelectedPlaybackVolume = 25;
        settings.SelectedWindowsSpeechRate = -3;
        settings.VolumeState.Effective.Should().Be(PlaybackVolume.Default);
        settings.RateState.Effective.Should().Be(WindowsSpeechRate.Default);
        fixture.VolumePreferences.Load().Should().BeNull();
        fixture.RatePreferences.Load().Should().BeNull();
        settings.Invoking(value => value.SelectedPlaybackVolume = 101).Should().Throw<ArgumentOutOfRangeException>();
        settings.Invoking(value => value.SelectedWindowsSpeechRate = 11).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Missing_configuration_is_explicitly_unavailable_and_optional_DI_resolution_does_not_create_capability()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher, AudioPlaybackSettingsFixture.QueuedDispatcher>();
        services.AddSingleton<ILogger<AudioPlaybackSettings>>(NullLogger<AudioPlaybackSettings>.Instance);
        services.AddSingleton<AudioPlaybackSettings>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var settings = provider.GetRequiredService<AudioPlaybackSettings>();
        settings.VolumeState.Should().BeNull();
        settings.RateState.Should().BeNull();
        settings.PlaybackVolumeStatus.Should().Contain("unavailable");
        settings.WindowsSpeechRateStatus.Should().Contain("unavailable");
        settings.ObserveWindowsSpeechRate();
        settings.HoldVolumeUnavailable();
        settings.HoldRateUnavailable();
        settings.Dispose();
        settings.Dispose();
        settings.ObserveWindowsSpeechRate();
    }

    [Fact]
    public async Task DI_resolves_the_actual_configuration_instances_and_observation_preserves_saved_values()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        fixture.VolumePreferences.Save(new(43));
        fixture.RatePreferences.Save(new(-4));
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher>(fixture.Dispatcher);
        services.AddSingleton<ILogger<AudioPlaybackSettings>>(NullLogger<AudioPlaybackSettings>.Instance);
        services.AddSingleton(fixture.Volume);
        services.AddSingleton(fixture.Rate);
        services.AddSingleton<AudioPlaybackSettings>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var settings = provider.GetRequiredService<AudioPlaybackSettings>();
        settings.SelectedPlaybackVolume.Should().Be(43);
        settings.SelectedWindowsSpeechRate.Should().Be(-4);
        settings.PlaybackVolumeStatus.Should().Contain("Saved on this device");
        settings.WindowsSpeechRateStatus.Should().Contain("Saved on this device");
        settings.VolumeState.Should().Be(fixture.Volume.Get());
        settings.RateState.Should().Be(fixture.Rate.Get());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_preferences_never_become_effective_defaults(bool rate)
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        fixture.Preferences.WriteLines(rate ? "speech-windows-rate.txt" : "speech-playback-volume.txt", ["unknown", "25"]);
        using var settings = fixture.CreateSettings();
        if (rate)
        {
            settings.RateState!.Effective.Should().BeNull();
            settings.RateState.Desired.Should().BeNull();
            settings.WindowsSpeechRateStatus.Should().Contain("unconfirmed");
            fixture.Playback.Rate.Should().BeNull();
            settings.VolumeState!.Available.Should().BeTrue();
        }
        else
        {
            settings.VolumeState!.Effective.Should().BeNull();
            settings.VolumeState.Desired.Should().BeNull();
            settings.PlaybackVolumeStatus.Should().Contain("unconfirmed");
            fixture.Playback.Volume.Should().BeNull();
            settings.RateState!.Available.Should().BeTrue();
        }
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observation_failure_remains_unavailable_until_explicit_refresh(bool unauthorized)
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        fixture.Preferences.ReadFailure = unauthorized ? new UnauthorizedAccessException("denied") : new IOException("unreadable");
        using var settings = fixture.CreateSettings();
        settings.VolumeState!.Effective.Should().BeNull();
        settings.RateState!.Effective.Should().BeNull();
        fixture.Preferences.ReadFailure = null;
        settings.RateState.Effective.Should().BeNull();
        settings.ObserveWindowsSpeechRate();
        fixture.Dispatcher.Drain();
        settings.RateState.Available.Should().BeTrue();
        settings.VolumeState.Effective.Should().BeNull();
    }

    [Fact]
    public async Task Rate_observation_invalid_operation_is_held_unavailable()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        fixture.Playback.InvalidationFailure = new InvalidOperationException("adapter unavailable");
        using var settings = new AudioPlaybackSettings(fixture.Dispatcher, NullLogger<AudioPlaybackSettings>.Instance, rate: fixture.Rate);
        settings.RateState!.Available.Should().BeFalse();
        fixture.Playback.InvalidationFailure = null;
        settings.ObserveWindowsSpeechRate();
        fixture.Dispatcher.Drain();
        settings.RateState.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Invalidation_is_immediate_but_selection_and_notifications_are_dispatched_and_retired_on_disposal()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var settings = fixture.CreateSettings();
        var invalidations = 0;
        var notifications = new List<AudioPlaybackSetting>();
        settings.OutputInvalidated += (_, _) => invalidations++;
        settings.Changed += (_, setting) => notifications.Add(setting);
        fixture.VolumePreferences.Save(new(20));
        fixture.RatePreferences.Save(new(2));
        fixture.Volume.Observe();
        fixture.Rate.Observe();
        invalidations.Should().Be(2);
        notifications.Should().BeEmpty();
        settings.SelectedPlaybackVolume.Should().Be(100);
        settings.SelectedWindowsSpeechRate.Should().Be(0);
        fixture.Dispatcher.Drain();
        notifications.Should().Equal(AudioPlaybackSetting.Volume, AudioPlaybackSetting.WindowsSpeechRate);
        settings.SelectedPlaybackVolume.Should().Be(20);
        settings.SelectedWindowsSpeechRate.Should().Be(2);
        fixture.VolumePreferences.Save(new(30));
        fixture.RatePreferences.Save(new(3));
        fixture.Volume.Observe();
        fixture.Rate.Observe();
        settings.Dispose();
        fixture.Dispatcher.Drain();
        notifications.Should().HaveCount(2);
        settings.SelectedPlaybackVolume.Should().Be(20);
        settings.SelectedWindowsSpeechRate.Should().Be(2);
        fixture.Volume.HoldUnavailable();
        fixture.Rate.HoldUnavailable();
        invalidations.Should().Be(4);
        settings.ObserveWindowsSpeechRate();
        settings.RateState!.Available.Should().BeFalse();
    }

    [Fact]
    public async Task An_already_captured_notification_cannot_reenter_a_disposed_owner()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        AudioPlaybackSettings? settings = null;
        fixture.Volume.Changed += (_, _) => settings?.Dispose();
        using (settings = fixture.CreateSettings())
        {
            var invalidations = 0;
            var notifications = 0;
            settings.OutputInvalidated += (_, _) => invalidations++;
            settings.Changed += (_, _) => notifications++;
            fixture.VolumePreferences.Save(new(25));
            fixture.Volume.Observe();
            fixture.Dispatcher.Drain();
            invalidations.Should().Be(0);
            notifications.Should().Be(0);
            settings.SelectedPlaybackVolume.Should().Be(100);
        }
    }
}
