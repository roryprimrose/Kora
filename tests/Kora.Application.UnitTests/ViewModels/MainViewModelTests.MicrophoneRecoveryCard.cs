using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Application.UnitTests.Maintenance;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Saved_unavailable_pin_survives_startup_and_metadata_refresh_without_a_substitute()
    {
        var f = CreateVoicePrivacyFixture();
        f.AudioPreferences.MicrophoneId = "missing-id";
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        await card.RefreshAsync();
        card.DisplayedSelection!.Device.Id.Should().Be("missing-id");
        card.DisplayedSelection.Label.Should().Contain("unavailable; saved pin retained");
        card.CanEnable.Should().BeFalse();
        f.AudioPreferences.MicrophoneId.Should().Be("missing-id");
        f.AudioPreferences.SavedMicrophoneId.Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Changed_System_default_during_validation_is_not_current_displayed_enable_authority()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var old = card.DisplayedSelection;
        old!.Label.Should().Contain("Headset [mic]");
        f.Voice.Microphones = [new("other", "Headset")];
        f.Voice.DefaultMicrophoneId = "other";
        await card.EnableAsync(old);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await card.RefreshAsync();
        card.DisplayedSelection!.Label.Should().Contain("[other]");
        await card.EnableAsync(card.DisplayedSelection);
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Card_view_draft_and_close_are_passive_and_fresh_selection_and_enable_are_distinct()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var changed = new List<string?>();
        card.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await card.RefreshAsync();
        card.Choices.Should().HaveCount(2);
        card.Status.Should().Contain("Current metadata reviewed");
        card.Readiness.Should().Contain("listening disabled");
        card.Consent.Should().Contain("retained");
        card.CanRefresh.Should().BeTrue();
        card.CanSave.Should().BeFalse();
        card.CanEnable.Should().BeTrue();
        var choice = card.Choices.Single(item => string.Equals(item.Device.Id, "mic", StringComparison.Ordinal));
        card.Draft = choice;
        card.CanSave.Should().BeTrue();
        card.CanEnable.Should().BeFalse();
        await card.EnableAsync(card.DisplayedSelection);
        card.Status.Should().Contain("Enable denied");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.Voice.StartCalls.Should().Be(0);
        await card.SaveAsync(choice);
        card.Status.Should().Contain("Saved preference only");
        card.Draft.Should().BeNull();
        card.DisplayedSelection!.Device.Should().Be(choice.Device);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        var saved = card.DisplayedSelection;
        await card.EnableAsync(saved);
        card.Status.Should().Contain("Push-to-talk is ready");
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
        await card.EnableAsync(saved);
        card.CanEnable.Should().BeFalse();
        await card.StopSpeakingAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        await card.DisableAsync();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        card.Draft = card.DisplayedSelection;
        card.CanEnable.Should().BeTrue();
        changed.Should().Contain(nameof(MicrophoneRecoveryViewModel.Choices));
        var audits = f.Audit.Events.Count;
        card.Dispose();
        card.Dispose();
        card.Choices.Should().BeEmpty();
        card.Draft.Should().BeNull();
        card.DisplayedSelection.Should().BeNull();
        card.Status.Should().Contain("Closed without consent");
        await card.RefreshAsync();
        await card.SaveAsync(choice);
        await card.EnableAsync(saved);
        await card.DisableAsync();
        await card.StopSpeakingAsync();
        f.Audit.Events.Should().HaveCount(audits);
        f.VoiceConsent.Consent.Should().BeTrue();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Recovery_with_no_consent_can_save_exact_choice_but_never_grants_or_enables(bool? consent)
    {
        var f = CreateVoicePrivacyFixture(consent);
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        card.Consent.Should().Contain("absent or withdrawn");
        card.CanEnable.Should().BeFalse();
        card.Draft = card.Choices[1];
        await card.SaveAsync(card.Draft);
        await card.EnableAsync(card.DisplayedSelection);
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        f.VoiceConsent.Consent.Should().Be(consent);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Forged_equal_choice_null_unavailable_and_stale_inputs_do_not_become_native_authority()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var old = card.Choices[1];
        card.Draft = old with { };
        card.CanSave.Should().BeFalse();
        await card.SaveAsync(card.Draft);
        await card.SaveAsync(null);
        await card.EnableAsync(null);
        await card.EnableAsync(old);
        card.Status.Should().Contain("Enable denied");
        await card.RefreshAsync();
        await card.SaveAsync(old);
        await card.EnableAsync(old);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        await card.SaveAsync(card.Choices[1]);
        f.Voice.Microphones = [new("other", "Headset")];
        f.Voice.DefaultMicrophoneId = "other";
        await card.RefreshAsync();
        var pin = card.Choices.Single(choice => string.Equals(choice.Device.Id, "mic", StringComparison.Ordinal));
        pin.Label.Should().Contain("unavailable; saved pin retained");
        card.Draft = pin;
        card.CanSave.Should().BeFalse();
        card.CanEnable.Should().BeFalse();
        await card.SaveAsync(pin);
        await card.EnableAsync(pin);
        f.AudioPreferences.MicrophoneId.Should().Be("mic");
        card.Draft = card.Choices.Single(choice => string.Equals(choice.Device.Id, "other", StringComparison.Ordinal));
        card.Draft.Label.Should().Contain("[other]");
        await card.SaveAsync(card.Draft);
        f.AudioPreferences.MicrophoneId.Should().Be("other");
    }

    [Fact]
    public async Task System_without_default_can_clear_pin_but_enable_remains_unavailable()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        await card.SaveAsync(card.Choices[1]);
        f.Voice.Microphones = [];
        f.Voice.DefaultMicrophoneId = null;
        await card.RefreshAsync();
        card.Choices[0].Label.Should().Be("System - Windows default; unavailable");
        card.CanEnable.Should().BeFalse();
        await card.SaveAsync(card.Choices[0]);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        card.CanEnable.Should().BeFalse();
        await card.EnableAsync(card.DisplayedSelection);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(MicrophoneAccessState.Unknown)]
    [InlineData(MicrophoneAccessState.Denied)]
    public async Task Permission_failure_is_explained_and_never_bypassed_by_card(MicrophoneAccessState access)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        f.MicrophoneAccess.Status = new(access, "synthetic");
        await card.RefreshAsync();
        card.CanEnable.Should().BeFalse();
        card.Readiness.Should().Contain("denied or unknown");
        await card.SaveAsync(card.Choices[1]);
        card.Status.Should().Contain("Selection was not saved");
        await card.EnableAsync(card.DisplayedSelection);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.VoiceConsent.Consent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_save_or_release_never_shows_a_successful_card_commit(bool release)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        if (release) { f.Voice.StopException = new IOException("owned release failure"); }
        else { f.AudioPreferences.MicrophoneSaveException = new IOException("owned save failure"); }
        await card.SaveAsync(card.Choices[1]);
        card.Status.Should().Contain("Selection was not saved");
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Disconnected)]
    public async Task Privacy_loss_clears_card_choices_without_consent_or_task_decisions(WindowsSessionState state)
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = state };
        await card.RefreshAsync();
        card.Choices.Should().BeEmpty();
        card.CanRefresh.Should().BeFalse();
        card.CanSave.Should().BeFalse();
        card.CanEnable.Should().BeFalse();
        card.Status.Should().Contain("ownership/privacy");
        f.VoiceConsent.Consent.Should().BeTrue();
    }

    [Fact]
    public async Task Ownership_loss_denies_refresh_and_never_exposes_old_choices()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        f.ViewModel.BindCallOwnershipGate(static () => false);
        await card.RefreshAsync();
        card.Choices.Should().BeEmpty();
        card.CanRefresh.Should().BeFalse();
    }

    [Fact]
    public async Task Protected_call_revisions_invalidate_card_input_without_converting_voice_origin()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var old = card.Choices[1];
        await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision,
            TestContext.Current.CancellationToken);
        card.CanEnable.Should().Be(f.ViewModel.IsVoiceActivationAvailable);
        await card.SaveAsync(old);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Recovery);
        await f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], f.ViewModel.MicrophoneTopologyRevision);
        await f.ViewModel.EnableListeningFromTrayAsync(f.ViewModel.MicrophoneTopologyRevision);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("save")]
    [InlineData("enable")]
    public async Task Closing_during_finite_metadata_work_retires_callbacks_and_pending_exact_input(string action)
    {
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, TimeProvider.System, NullLogger.Instance);
        var f = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var operation = action switch
        {
            "save" => card.SaveAsync(card.Choices[1]),
            "enable" => card.EnableAsync(card.DisplayedSelection),
            _ => card.RefreshAsync(),
        };
        card.CanRefresh.Should().BeFalse();
        await card.RefreshAsync();
        card.Dispose();
        completion.SetResult(TraySnapshot());
        await operation;
        card.Choices.Should().BeEmpty();
        card.Status.Should().Contain("Closed without consent");
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Refresh_timeout_error_and_late_results_do_not_resurrect_card_choices_or_enablement()
    {
        var clock = new ReleaseFixture.Clock();
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, clock, NullLogger.Instance);
        var f = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await f.ViewModel.InitializeAsync();
        using var card = new MicrophoneRecoveryViewModel(f.ViewModel);
        var refresh = card.RefreshAsync();
        card.Choices.Should().BeEmpty();
        clock.Timers[0].Fire();
        await refresh;
        card.Choices.Should().BeEmpty();
        card.CanEnable.Should().BeFalse();
        card.Status.Should().Contain("Microphone recovery needs attention");
        completion.SetException(new IOException("late enumeration"));
        await card.RefreshAsync();
        card.Status.Should().Contain("Microphone recovery needs attention");
        card.Choices.Should().BeEmpty();
        f.VoiceConsent.Consent.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_rejects_call_revision_change_during_validation_even_with_current_endpoint(bool enable)
    {
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new BoundedMicrophoneCatalog(() => completion.Task, TimeProvider.System, NullLogger.Instance);
        var f = CreateVoicePrivacyFixture(microphoneCatalog: catalog);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        var revision = f.ViewModel.MicrophoneTopologyRevision;
        var operation = enable ? f.ViewModel.EnableListeningFromTrayAsync(revision)
            : f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], revision);
        await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision,
            TestContext.Current.CancellationToken);
        completion.SetResult(TraySnapshot());
        await operation;
        f.AudioPreferences.MicrophoneId.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Displayed_enable_endpoint_and_newly_enumerated_choices_are_exact_not_name_authority()
    {
        var f = CreateVoicePrivacyFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.DisableListeningFromTrayAsync();
        var revision = f.ViewModel.MicrophoneTopologyRevision;
        await f.ViewModel.EnableListeningFromRecoveryAsync(revision, f.ViewModel.Microphones[1],
            TestContext.Current.CancellationToken);
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.Voice.Microphones = [new("other", "Headset")];
        f.Voice.DefaultMicrophoneId = "other";
        await f.ViewModel.SelectMicrophoneAsync(f.ViewModel.Microphones[1], revision);
        f.AudioPreferences.MicrophoneId.Should().BeNull();
    }
}
