using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed partial class AudioPlaybackSettingsCommandTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public AudioPlaybackSettingsCommandTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Exact_reset_and_relative_changes_share_typed_configuration_and_audit_without_playback()
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var settings = fixture.CreateSettings();
        var token = TestContext.Current.CancellationToken;
        var volume = await settings.ExecuteVolumeAsync(new(AppearanceCommandOperation.Set, "35"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, token);
        volume.Saved.Should().BeTrue();
        volume.Outcome.Should().Be("saved");
        settings.SelectedPlaybackVolume.Should().Be(35);
        fixture.VolumePreferences.Load().Should().Be(new PlaybackVolume(35));
        var rate = await settings.ExecuteRateAsync(new(AppearanceCommandOperation.Set, "2"), RequestOrigin.LocalUi,
            SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, token);
        rate.Saved.Should().BeTrue();
        settings.SelectedWindowsSpeechRate.Should().Be(2);
        fixture.RatePreferences.Load().Should().Be(new WindowsSpeechRate(2));

        volume = await settings.ExecuteVolumeAsync(new(AppearanceCommandOperation.Set) { Adjustment = AudioPreferenceAdjustment.Decrease },
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, token);
        volume.PreviousDesired.Should().Be(new PlaybackVolume(35));
        settings.SelectedPlaybackVolume.Should().Be(25);
        rate = await settings.ExecuteRateAsync(new(AppearanceCommandOperation.Set) { Adjustment = AudioPreferenceAdjustment.Decrease },
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, token);
        rate.PreviousDesired.Should().Be(new WindowsSpeechRate(2));
        settings.SelectedWindowsSpeechRate.Should().Be(1);
        await settings.ExecuteVolumeAsync(new(AppearanceCommandOperation.Reset), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, token);
        await settings.ExecuteRateAsync(new(AppearanceCommandOperation.Reset), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, fixture.Policy, static () => true, token);
        fixture.VolumePreferences.Load().Should().BeNull();
        fixture.RatePreferences.Load().Should().BeNull();
        settings.SelectedPlaybackVolume.Should().Be(100);
        settings.SelectedWindowsSpeechRate.Should().Be(0);
        fixture.Audit.Events.Select(item => item.Outcome).Should().Equal(
            Enumerable.Range(0, 6).SelectMany(_ => new[] { SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded }));
        settings.IsVolumeControlActive.Should().BeFalse();
        settings.IsRateControlActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_persistence_is_explicit_and_cannot_publish_an_effective_setting(bool rate)
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var settings = fixture.CreateSettings();
        fixture.Preferences.WriteFailure = new IOException("write failed");
        await FluentActions.Awaiting(() => Execute(settings, fixture, rate)).Should().ThrowAsync<IOException>();
        fixture.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        if (rate) { settings.RateState!.Available.Should().BeFalse(); }
        else { settings.VolumeState!.Available.Should().BeFalse(); }
        settings.IsVolumeControlActive.Should().BeFalse();
        settings.IsRateControlActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "revoked")]
    [InlineData(true, "revoked")]
    [InlineData(false, "canceled")]
    [InlineData(true, "canceled")]
    [InlineData(false, "disposed")]
    [InlineData(true, "disposed")]
    public async Task Pending_admission_cannot_complete_after_revocation_cancellation_or_disposal(bool rate, string change)
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var settings = fixture.CreateSettings();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = new TaskCompletionSource<WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Store.PendingCreation = pending.Task;
        var eligible = true;
        var notifications = 0;
        settings.Changed += (_, _) => notifications++;
        var operation = Execute(settings, fixture, rate, () => eligible, cancellation.Token);
        (rate ? settings.IsRateControlActive : settings.IsVolumeControlActive).Should().BeTrue();
        await FluentActions.Awaiting(() => Execute(settings, fixture, rate)).Should().ThrowAsync<InvalidOperationException>();
        if (change is "revoked") { eligible = false; }
        if (change is "canceled") { await cancellation.CancelAsync(); }
        if (change is "disposed") { settings.Dispose(); }
        pending.SetResult(fixture.Store.Authority!);
        if (change is "canceled")
        {
            await FluentActions.Awaiting(() => operation).Should().ThrowAsync<OperationCanceledException>();
        }
        else { await FluentActions.Awaiting(() => operation).Should().ThrowAsync<InvalidOperationException>(); }
        fixture.Dispatcher.Drain();
        fixture.VolumePreferences.Load().Should().BeNull();
        fixture.RatePreferences.Load().Should().BeNull();
        fixture.Audit.Events.Should().BeEmpty();
        settings.IsVolumeControlActive.Should().BeFalse();
        settings.IsRateControlActive.Should().BeFalse();
        if (change is "disposed") { notifications.Should().Be(0); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_configuration_disposal_and_missing_exact_values_fail_explicitly(bool rate)
    {
        await using var fixture = new AudioPlaybackSettingsFixture();
        using var missing = new AudioPlaybackSettings(fixture.Dispatcher, NullLogger<AudioPlaybackSettings>.Instance);
        await FluentActions.Awaiting(() => Execute(missing, fixture, rate)).Should().ThrowAsync<InvalidOperationException>();
        missing.Dispose();
        await FluentActions.Awaiting(() => Execute(missing, fixture, rate)).Should().ThrowAsync<ObjectDisposedException>();
        using var settings = fixture.CreateSettings();
        var token = TestContext.Current.CancellationToken;
        if (rate)
        {
            await FluentActions.Awaiting(() => settings.ExecuteRateAsync(new(AppearanceCommandOperation.Set), RequestOrigin.LocalUi,
                SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, token)).Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await FluentActions.Awaiting(() => settings.ExecuteVolumeAsync(new(AppearanceCommandOperation.Set), RequestOrigin.LocalUi,
                SecurityAuditInitiator.LocalUser, fixture.Policy, static () => true, token)).Should().ThrowAsync<InvalidOperationException>();
        }
        fixture.Audit.Events.Should().BeEmpty();
    }

    private static async Task Execute(AudioPlaybackSettings settings, AudioPlaybackSettingsFixture fixture, bool rate,
        Func<bool>? eligible = null, CancellationToken token = default)
    {
        if (rate)
        {
            await settings.ExecuteRateAsync(new(AppearanceCommandOperation.Set, "2"), RequestOrigin.LocalUi,
                SecurityAuditInitiator.LocalUser, fixture.Policy, eligible ?? (static () => true), token);
        }
        else
        {
            await settings.ExecuteVolumeAsync(new(AppearanceCommandOperation.Set, "20"), RequestOrigin.LocalUi,
                SecurityAuditInitiator.LocalUser, fixture.Policy, eligible ?? (static () => true), token);
        }
    }
}
