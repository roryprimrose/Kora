using AwesomeAssertions;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Application.Voice;
using Kora.Application.UnitTests.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static MicrophoneCatalogSnapshot TraySnapshot() => new([new("mic", "Headset")], new("mic", "Headset"),
        new(WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 0, ["mic"], "mic", null),
        new(MicrophoneAccessState.Allowed, "synthetic"));

    [Fact]
    public async Task Tray_consent_and_unacknowledged_capture_status_remain_truthful()
    {
        var fixture = CreateVoicePrivacyFixture(false);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.TrayInputStatus.Should().Contain("consent not granted");
        fixture.ViewModel.IsSystemMicrophoneAvailable.Should().BeTrue();
        await fixture.ViewModel.SetVoiceConsentAsync(true);
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.CompleteCapture();
        fixture.ViewModel.TrayInputStatus.Should().Contain("microphone closed");
    }

    [Fact]
    public async Task An_owned_refresh_deadline_is_measured_and_coalesces_without_accepting_late_metadata()
    {
        var clock = new ReleaseFixture.Clock();
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, clock, NullLogger.Instance);
        var fixture = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await fixture.ViewModel.InitializeAsync();
        var refresh = fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.RefreshMicrophonesAsync().Should().BeSameAs(refresh);
        clock.Timers.Should().ContainSingle();
        clock.Timers[0].Due.Should().Be(TimeSpan.FromSeconds(5));
        clock.Timers[0].Fire();
        await refresh;
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Microphone recovery needs attention.");
        completion.SetResult(TraySnapshot());
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Late_validation_and_observer_callbacks_cannot_update_a_disposed_host()
    {
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, TimeProvider.System, NullLogger.Instance);
        var fixture = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await fixture.ViewModel.InitializeAsync();
        var publisher = fixture.PrivacyObservation.CapturePublisher();
        var selection = fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1],
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.Dispose();
        completion.SetResult(TraySnapshot());
        await selection;
        publisher(new(fixture.PrivacyObservation.Current, fixture.PrivacyObservation.Current,
            WindowsPrivacyChangeReason.Session));
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_during_a_completed_enumeration_error_does_not_restore_presentation(bool validating)
    {
        Fixture? fixture = null;
        var catalog = new BoundedMicrophoneCatalog(() =>
        {
            fixture!.ViewModel.Dispose();
            return Task.FromException<MicrophoneCatalogSnapshot>(new IOException("owned late error"));
        }, TimeProvider.System, NullLogger.Instance);
        fixture = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await fixture.ViewModel.InitializeAsync();
        if (validating)
        {
            await fixture.ViewModel.EnableListeningFromTrayAsync(fixture.ViewModel.MicrophoneTopologyRevision);
        }
        else
        {
            await fixture.ViewModel.RefreshMicrophonesAsync();
        }
        fixture.ViewModel.ResponseTitle.Should().NotBe("Microphone recovery needs attention.");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Tray_status_never_contains_endpoint_identity_or_transcript_and_distinguishes_ready_from_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        fixture.Voice.Microphones = [new("private-id", "Private endpoint name")];
        fixture.Voice.DefaultMicrophoneId = "private-id";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.TrayInputStatus.Should().Be("Push-to-talk ready - microphone closed - wake unavailable");
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.TrayInputStatus.Should().Be("Push-to-talk capture active");
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        fixture.ViewModel.TrayInputStatus.Should().Be("Microphone closed - listening disabled");
        fixture.ViewModel.TrayInputStatus.Should().NotContain("Private").And.NotContain("private-id");
    }

    [Fact]
    public async Task Current_selection_saves_preference_releases_capture_and_never_implicitly_reenables()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.RefreshMicrophonesAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        var device = fixture.ViewModel.Microphones[1];
        await fixture.ViewModel.SelectMicrophoneAsync(device, fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.SelectedMicrophone.Should().Be(device);
        fixture.AudioPreferences.MicrophoneId.Should().Be(device.Id);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.Voice.StopCalls.Should().BeGreaterThan(0);

        await fixture.ViewModel.RefreshMicrophonesAsync();
        var revision = fixture.ViewModel.MicrophoneTopologyRevision;
        await fixture.ViewModel.EnableListeningFromTrayAsync(revision);
        await fixture.ViewModel.EnableListeningFromTrayAsync(revision);
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.Voice.StartCalls.Should().Be(1);
        await fixture.ViewModel.SelectMicrophoneAsync(SystemAudioDevices.Microphone, fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.AudioPreferences.MicrophoneId.Should().BeNull();
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    public async Task Native_selection_and_enable_fail_closed_on_authoritative_privacy_not_only_session_controller(
        WindowsSessionState state)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        var revision = fixture.ViewModel.MicrophoneTopologyRevision;
        fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { SessionState = state };
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1], revision);
        await fixture.ViewModel.EnableListeningFromTrayAsync(revision);
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.TrayInputStatus.Should().Contain("privacy recovery");
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData(MicrophoneAccessState.Unknown)]
    [InlineData(MicrophoneAccessState.Denied)]
    public async Task Permission_cannot_be_replaced_by_device_selection_or_enable(MicrophoneAccessState state)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        fixture.MicrophoneAccess.Status = new(state, "synthetic");
        await fixture.ViewModel.RefreshMicrophonesAsync();
        var revision = fixture.ViewModel.MicrophoneTopologyRevision;
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1], revision);
        await fixture.ViewModel.EnableListeningFromTrayAsync(revision);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.TrayInputStatus.Should().Contain("denied or unknown");
    }

    [Fact]
    public async Task Lost_pinned_endpoint_remains_selected_and_System_does_not_silently_replace_it()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var pinned = fixture.ViewModel.Microphones[1];
        await fixture.ViewModel.SelectMicrophoneAsync(pinned, fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.Voice.Microphones = [new("other", pinned.Name)];
        fixture.Voice.DefaultMicrophoneId = "other";
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.SelectedMicrophone.Should().Be(pinned);
        fixture.ViewModel.Microphones.Should().NotContain(pinned);
        fixture.ViewModel.TrayInputStatus.Should().Contain("selected microphone unavailable");
        await fixture.ViewModel.EnableListeningFromTrayAsync(fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Explicit_System_selection_can_clear_an_unavailable_pin_without_a_default_or_capture()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1],
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.Voice.Microphones = [];
        fixture.Voice.DefaultMicrophoneId = null;
        await fixture.ViewModel.RefreshMicrophonesAsync();
        await fixture.ViewModel.SelectMicrophoneAsync(SystemAudioDevices.Microphone,
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.AudioPreferences.MicrophoneId.Should().BeNull();
        fixture.ViewModel.IsSystemMicrophoneAvailable.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }
    [Fact]
    public async Task Stale_enable_after_disable_or_configuration_change_does_not_toggle_state()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        var oldRevision = fixture.ViewModel.MicrophoneTopologyRevision;
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        await fixture.ViewModel.EnableListeningFromTrayAsync(oldRevision);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Listening cannot be enabled.");
        fixture.ViewModel.BindCallOwnershipGate(static () => false);
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.CanUseTrayMicrophoneRecovery.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_speaking_does_not_disable_input_or_change_microphone_preference()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.StopSpeakingFromTrayAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
    }

    [Fact]
    public async Task Late_refresh_and_disposed_callbacks_cannot_restore_or_mutate_recovery_state()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.BeforeEnumeration = fixture.ViewModel.Dispose;
        await fixture.ViewModel.RefreshMicrophonesAsync();
        await fixture.ViewModel.RefreshMicrophonesAsync();
        await fixture.ViewModel.EnableListeningFromTrayAsync(fixture.ViewModel.MicrophoneTopologyRevision);
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1], fixture.ViewModel.MicrophoneTopologyRevision);
        await fixture.ViewModel.DisableListeningFromTrayAsync();
        await fixture.ViewModel.StopSpeakingFromTrayAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_retired_by_a_privacy_hold_cannot_release_that_hold_or_publish_late_metadata()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.Voice.BeforeEnumeration = () => fixture.ViewModel.CloseForObservedPrivacyEvent("synthetic loss", false);
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.IsMicrophoneCatalogCurrent.Should().BeFalse();
        fixture.ViewModel.TrayInputStatus.Should().Contain("refresh microphones");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Privacy_observation_failure_reports_recoverable_error_without_selection()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.PrivacyObservation.RefreshException = new IOException("synthetic observer failure");
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1], fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selection_save_or_release_failure_cannot_claim_success(bool releaseFailure)
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        if (releaseFailure)
        {
            fixture.Voice.StopException = new IOException("synthetic release failure");
        }
        else
        {
            fixture.AudioPreferences.MicrophoneSaveException = new IOException("synthetic save failure");
        }
        await fixture.ViewModel.SelectMicrophoneAsync(fixture.ViewModel.Microphones[1],
            fixture.ViewModel.MicrophoneTopologyRevision);
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be(releaseFailure
            ? "Microphone cleanup needs attention." : "The microphone preference could not be saved.");
        if (!releaseFailure)
        {
            fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
        }
    }

    [Fact]
    public async Task Refresh_endpoint_loss_closes_capture_even_without_an_observer_notification()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.Microphones = [];
        fixture.Voice.DefaultMicrophoneId = null;
        await fixture.ViewModel.RefreshMicrophonesAsync();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.IsListening.Should().BeFalse();
        fixture.ViewModel.SelectedMicrophone.Should().Be(SystemAudioDevices.Microphone);
    }
}
