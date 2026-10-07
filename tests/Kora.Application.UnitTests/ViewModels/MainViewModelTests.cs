using System.Globalization;
using System.Reflection;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.ViewModels;
using Kora.Application.Hosting;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

[Collection("Host tracing")]
public sealed partial class MainViewModelTests : IDisposable
{
    private readonly ActivityListener hostListener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public MainViewModelTests() => ActivitySource.AddActivityListener(hostListener);

    public void Dispose() => hostListener.Dispose();

    [Fact]
    public void Command_failure_remains_visible_when_required_error_logging_itself_fails()
    {
        var failedLogger = new FailedEvidenceLogger();
        var fixture = new Fixture(logger: failedLogger);
        failedLogger.Fail = true;
        var handler = typeof(MainViewModel).GetMethod("HandleCommandException", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var invoke = () => handler.Invoke(fixture.ViewModel, [new IOException("fixture command failure")]);
        invoke.Should().Throw<TargetInvocationException>().Which.InnerException.Should().BeOfType<IOException>();
        fixture.ViewModel.ResponseTitle.Should().Be("The command failed.");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
    }

    [Fact]
    public void Durable_version_admission_requires_each_current_host_and_interaction_boundary()
    {
        foreach (var host in new[] { false, true })
        foreach (var question in new[] { false, true })
        foreach (var grant in new[] { false, true })
        foreach (var approval in new[] { false, true })
        {
            MainViewModel.IsDurableVersionQueryEligible(host, question, grant, approval)
                .Should().Be(host && !question && !grant && !approval);
        }
    }

    [Fact]
    public async Task Exact_version_request_has_durable_receipt_and_readable_copy_disclosure()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, what version are you running");
        fixture.HostStore.Records.Select(record => record.State).Should().Equal(
            HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Succeeded);
        fixture.ViewModel.LocalStorageDisclosure.Should().Be(DurableVersionQuery.StorageDisclosure);
        fixture.ViewModel.ResponseBody.Should().Contain(DurableVersionQuery.StorageDisclosure);
    }

    [Fact]
    public async Task Changed_privacy_before_durable_query_dispatch_cannot_record_success()
    {
        var fixture = new Fixture();
        fixture.HostStore.BeforeCommit = record =>
        {
            if (record.State == HostTaskState.DispatchRecorded)
            {
                fixture.Session.IsUnlocked = false;
            }
        };
        var run = () => fixture.RunAsync("Kora, what version are you running");
        await run.Should().ThrowAsync<InvalidOperationException>();
        fixture.HostStore.Records.Last().State.Should().Be(HostTaskState.Failed);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Kora version");
    }

    [Fact]
    public void Grant_editor_visual_policy_covers_each_independent_reason_to_show_the_response()
    {
        foreach (var editorVisible in new[] { false, true })
        foreach (var interactionPending in new[] { false, true })
        foreach (var failed in new[] { false, true })
        {
            var state = failed ? AssistantState.Failure : AssistantState.Information;
            MainViewModel.ShouldForceVisualResponse(
                editorVisible, interactionPending, state)
                .Should().Be(editorVisible || interactionPending || failed);
        }

        foreach (var visible in new[] { false, true })
        foreach (var initializing in new[] { false, true })
        {
            MainViewModel.ShouldShowGrantEditor(visible, initializing)
                .Should().Be(visible && !initializing);
        }
    }

    [Fact]
    public void Reassigning_private_setup_and_busy_state_does_not_raise_change_notifications()
    {
        var fixture = new Fixture();
        var changes = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        foreach (var property in new[]
        {
            nameof(MainViewModel.IsBusy),
            nameof(MainViewModel.IsLocalModelSetupActive),
            nameof(MainViewModel.IsPowerShellSetupActive),
        })
        {
            typeof(MainViewModel).GetProperty(property)!.SetValue(fixture.ViewModel, false);
        }

        changes.Should().BeEmpty();
        fixture.ViewModel.CanInstallLocalModel.Should().BeTrue();
        fixture.ViewModel.CanInstallPowerShell.Should().BeTrue();
    }

    [Fact]
    public void Grant_change_validity_matches_each_operation_scope_and_existing_grant()
    {
        foreach (var inSession in new[] { false, true })
        foreach (var inAlways in new[] { false, true })
        foreach (var scope in new[] { ModelApprovalScope.Session, ModelApprovalScope.Always })
        foreach (var operation in Enum.GetValues<GrantChangeOperation>())
        foreach (var target in new ModelApprovalScope?[]
            { null, ModelApprovalScope.Session, ModelApprovalScope.Always })
        {
            var exists = scope == ModelApprovalScope.Session ? inSession : inAlways;
            var targetExists = target == ModelApprovalScope.Session ? inSession : inAlways;
            var applicable = operation switch
            {
                GrantChangeOperation.Add => !exists && target is null,
                GrantChangeOperation.Remove => exists && target is null,
                GrantChangeOperation.Move => exists && target is not null
                    && target != scope && !targetExists,
                _ => false,
            };
            var change = new GrantChange(operation, BuiltInAction.LockMachine, scope, target);
            MainViewModel.IsGrantChangeInapplicable(change, scope, inSession, inAlways)
                .Should().Be(!applicable);
        }
    }

    [Fact]
    public void Defensive_model_decisions_reject_unsupported_inputs_without_changing_permissions()
    {
        MainViewModel.DescribeGrantChange(
            new GrantChange(GrantChangeOperation.Move, BuiltInAction.LockMachine,
                ModelApprovalScope.Session, ModelApprovalScope.Always))
            .Should().Contain("Move LockMachine");
        var invalidChange = () => MainViewModel.DescribeGrantChange(new GrantChange(
            (GrantChangeOperation)9999, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        invalidChange.Should().Throw<ArgumentOutOfRangeException>();

        foreach (var action in Enum.GetValues<BuiltInAction>())
        {
            var requiresApproval = action is BuiltInAction.HideApplication
                or BuiltInAction.ExitApplication or BuiltInAction.RestartApplication
                or BuiltInAction.CancelTask or BuiltInAction.LockMachine
                or BuiltInAction.ProposeShutdown or BuiltInAction.ProposeRestart
                or BuiltInAction.EnableLocalModels or BuiltInAction.DisableLocalModels
                or BuiltInAction.EnableHostedModels or BuiltInAction.DisableHostedModels;
            MainViewModel.ModelActionRequiresApproval(action).Should().Be(requiresApproval);
        }
        var invalidAction = () => MainViewModel.ModelActionRequiresApproval((BuiltInAction)9999);
        invalidAction.Should().Throw<ArgumentOutOfRangeException>();

        MainViewModel.ScopeForApprovalReply(ModelApprovalReply.Once).Should().Be(ModelApprovalScope.Once);
        MainViewModel.ScopeForApprovalReply(ModelApprovalReply.Session).Should().Be(ModelApprovalScope.Session);
        MainViewModel.ScopeForApprovalReply(ModelApprovalReply.Always).Should().Be(ModelApprovalScope.Always);
        var invalidReply = () => MainViewModel.ScopeForApprovalReply(ModelApprovalReply.Reject);
        invalidReply.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Model_action_guard_rejects_invalid_scopes_missing_actions_and_each_busy_state()
    {
        foreach (var scope in Enum.GetValues<ModelApprovalScope>())
        {
            MainViewModel.ValidateModelActionApproval(scope, BuiltInAction.LockMachine,
                false, false, false).Should().Be(BuiltInAction.LockMachine);
        }
        var invalidScope = () => MainViewModel.ValidateModelActionApproval(
            (ModelApprovalScope)9999, BuiltInAction.LockMachine, false, false, false);
        var noAction = () => MainViewModel.ValidateModelActionApproval(
            ModelApprovalScope.Once, null, false, false, false);
        invalidScope.Should().Throw<ArgumentOutOfRangeException>();
        noAction.Should().Throw<InvalidOperationException>();

        for (var states = 1; states < 8; states++)
        {
            var busy = (states & 1) != 0;
            var modelSetup = (states & 2) != 0;
            var powerShellSetup = (states & 4) != 0;
            var action = () => MainViewModel.ValidateModelActionApproval(
                ModelApprovalScope.Once, BuiltInAction.LockMachine,
                busy, modelSetup, powerShellSetup);
            action.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void Local_reasoning_busy_guard_checks_each_active_operation()
    {
        for (var states = 0; states < 64; states++)
        {
            var result = MainViewModel.IsLocalReasoningBusy(
                (states & 1) != 0, (states & 2) != 0, (states & 4) != 0,
                (states & 8) != 0, (states & 16) != 0, (states & 32) != 0);
            result.Should().Be(states != 0);
        }
    }

    [Fact]
    public async Task Busy_command_filter_distinguishes_status_from_other_and_unmatched_commands()
    {
        var fixture = new Fixture();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = fixture.ViewModel.DetectMicrophonesAsync();
        fixture.ViewModel.IsBusy.Should().BeTrue();

        fixture.ViewModel.CommandText = "Kora, what are you currently working on";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        fixture.ViewModel.CommandText = "Kora, open settings";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();
        fixture.ViewModel.CommandText = "an unsupported question";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeFalse();

        fixture.Probe.Gate.SetResult();
        await refreshing;
    }

    [Fact]
    public async Task InitializeAsync_with_saved_consent_arms_PTT_without_ambient_capture()
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
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.Voice.StartedMicrophone.Should().BeNull();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.MicrophoneAccessStatus.State.Should().Be(MicrophoneAccessState.Allowed);
        fixture.ViewModel.MicrophoneAccessMessage.Should().Contain("allowed");
        fixture.ViewModel.ResponseTitle.Should().Be("Push-to-talk is ready.");
        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, false)]
    [InlineData(DependencyReadiness.Missing, true)]
    public async Task InitializeAsync_presents_detected_local_model_status_without_setup_review(
        DependencyReadiness readiness,
        bool shouldOfferSetup)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference",
            "Local model inference (Ollama)",
            readiness,
            "Detected local model status.");

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.LocalModelSetupStatus.Should().Be("Detected local model status.");
        fixture.ViewModel.ShouldOfferLocalModelSetup.Should().Be(shouldOfferSetup);
        fixture.ViewModel.ShouldOfferPowerShellSetup.Should().BeFalse();
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, false)]
    [InlineData(DependencyReadiness.Missing, true)]
    public async Task InitializeAsync_presents_detected_PowerShell_status_without_setup_review(
        DependencyReadiness readiness,
        bool shouldOfferSetup)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "powershell.runtime",
            "PowerShell 7 (pwsh)",
            readiness,
            "Detected PowerShell status.");

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PowerShellSetupStatus.Should().Be("Detected PowerShell status.");
        fixture.ViewModel.ShouldOfferPowerShellSetup.Should().Be(shouldOfferSetup);
        fixture.ViewModel.ShouldOfferLocalModelSetup.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_voice_revalidates_selection_at_execution_time()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.SelectedVoice = null;
        var previewVoice = typeof(MainViewModel).GetMethod(
            "PreviewVoiceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        previewVoice.Should().NotBeNull();
        await (Task)previewVoice!.Invoke(fixture.ViewModel, null)!;

        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("Voice preview is unavailable.");
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
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
    public async Task First_launch_greets_the_user_and_explains_explicit_voice_consent()
    {
        var fixture = new Fixture();
        fixture.VoiceConsent.Consent = null;
        fixture.UserName.AddressName = "Rory";
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        var settingsRequests = 0;
        var recoveryRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        fixture.ViewModel.VoiceRecoveryRequested += (_, _) => recoveryRequests++;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Hi Rory, I'm Kora.");
        fixture.ViewModel.ResponseBody.Should().Contain("grant explicit voice consent");
        fixture.ViewModel.ResponseBody.Should().Contain("microphone stays closed");
        fixture.ViewModel.ResponseBody.Should().Contain("continue without voice");
        fixture.ViewModel.NeedsVoiceConsent.Should().BeTrue();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        var responseAction = fixture.ViewModel.ResponseActions.Should().ContainSingle().Which;
        responseAction.Kind.Should().Be(ResponseActionKind.OpenVoiceSettings);
        responseAction.Label.Should().Be("Review voice settings");
        fixture.ViewModel.HasResponseActions.Should().BeTrue();
        settingsRequests.Should().Be(0);
        recoveryRequests.Should().Be(0);
        fixture.Voice.StartCalls.Should().Be(0);

        await fixture.ViewModel.ExecuteResponseActionAsync(new ResponseAction(
            ResponseActionKind.OpenVoiceSettings,
            responseAction.Label));

        fixture.ViewModel.HasResponseActions.Should().BeTrue();
        settingsRequests.Should().Be(0);
        recoveryRequests.Should().Be(0);

        await fixture.ViewModel.ExecuteResponseActionAsync(responseAction);

        fixture.ViewModel.ResponseActions.Should().BeEmpty();
        fixture.ViewModel.HasResponseActions.Should().BeFalse();
        settingsRequests.Should().Be(1);
        recoveryRequests.Should().Be(1);
    }

    [Fact]
    public async Task First_launch_greeting_identifies_blocked_Windows_microphone_access()
    {
        var fixture = new Fixture();
        fixture.VoiceConsent.Consent = null;
        fixture.UserName.AddressName = null;
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(
            MicrophoneAccessState.Denied,
            "Windows microphone access is blocked for desktop apps.");

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Hi, I'm Kora.");
        fixture.ViewModel.ResponseBody.Should().Contain("enable Windows microphone access");
        fixture.ViewModel.ResponseBody.Should().Contain("grant explicit voice consent");
        fixture.ViewModel.ResponseActions.Should().ContainSingle()
            .Which.Kind.Should().Be(ResponseActionKind.OpenVoiceSettings);
    }

    [Theory]
    [InlineData(false, "connect a microphone")]
    [InlineData(true, "confirm Windows microphone access")]
    public async Task First_launch_greeting_explains_missing_or_unknown_microphone_access(
        bool hasMicrophone,
        string expectedGuidance)
    {
        var fixture = new Fixture();
        fixture.VoiceConsent.Consent = null;
        fixture.UserName.AddressName = " ";
        if (hasMicrophone)
        {
            fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
            fixture.Voice.DefaultMicrophoneId = "0";
        }
        fixture.MicrophoneAccess.Status = new MicrophoneAccessStatus(
            MicrophoneAccessState.Unknown,
            "Windows microphone access could not be verified.");

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Hi, I'm Kora.");
        fixture.ViewModel.ResponseBody.Should().Contain(expectedGuidance);
        fixture.ViewModel.ResponseBody.Should().Contain("grant explicit voice consent");
        fixture.ViewModel.ResponseBody.Should().Contain("continue without voice");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.ResponseActions.Should().ContainSingle()
            .Which.Kind.Should().Be(ResponseActionKind.OpenVoiceSettings);
    }

    [Fact]
    public async Task Greeting_action_opens_settings_without_a_voice_recovery_subscriber()
    {
        var fixture = new Fixture();
        fixture.VoiceConsent.Consent = null;
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        await fixture.ViewModel.InitializeAsync();
        var action = fixture.ViewModel.ResponseActions.Should().ContainSingle().Which;

        await fixture.ViewModel.ExecuteResponseActionAsync(action);
        await fixture.ViewModel.ExecuteResponseActionAsync(action);

        settingsRequests.Should().Be(1);
        fixture.ViewModel.ResponseActions.Should().BeEmpty();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        fixture.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Dismissing_the_greeting_invalidates_its_response_action()
    {
        var fixture = new Fixture();
        fixture.VoiceConsent.Consent = null;
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        await fixture.ViewModel.InitializeAsync();
        var action = fixture.ViewModel.ResponseActions.Should().ContainSingle().Which;

        fixture.ViewModel.HideApplication();
        await fixture.ViewModel.ExecuteResponseActionAsync(action);

        fixture.ViewModel.HasResponseActions.Should().BeFalse();
        settingsRequests.Should().Be(0);
    }

    [Fact]
    public async Task Response_action_rejects_null()
    {
        var fixture = new Fixture();
        var action = () => fixture.ViewModel.ExecuteResponseActionAsync(null!);

        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Response_action_rejects_an_unknown_host_action_kind()
    {
        var fixture = new Fixture();
        var responseAction = new ResponseAction((ResponseActionKind)int.MaxValue, "Invalid");
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.ResponseActions))!
            .SetValue(fixture.ViewModel, new[] { responseAction });
        var action = () => fixture.ViewModel.ExecuteResponseActionAsync(responseAction);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unknown response action:*");
        fixture.ViewModel.ResponseActions.Should().BeEmpty();
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reinitializing_while_listening_does_not_start_capture_again()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";

        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.InitializeAsync();

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(StartFailures))]
    public async Task Explicit_PTT_surfaces_capture_open_failures(
        Exception exception,
        string expectedTitle)
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        fixture.Voice.StartException = exception;

        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Enabling_listening_contains_unexpected_readiness_failures()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PrivacyObservation.RefreshException = new IOException("privacy observation failed");

        Func<Task> action = () => fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        await action.Should().NotThrowAsync();
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("Voice activation failed.");
        fixture.ViewModel.ResponseBody.Should().Contain("privacy observation failed");
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
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
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(10);
        fixture.ViewModel.PresenceTimeoutDescription.Should().Contain("Hide the presence");
        fixture.ViewModel.PresenceTimeoutDescription.Should().Contain("inactivity")
            .And.Contain("no prompts").And.NotContain("response window");
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(5);
        fixture.ViewModel.ResponseTimeoutDescription.Should().Contain("unpinned response window");
        fixture.ViewModel.IsResponseAlwaysVisible.Should().BeFalse();
        fixture.ViewModel.IsResponseWindowTopmost.Should().BeTrue();
        fixture.ViewModel.ResponseWindowPosition.Should().BeNull();
        fixture.ViewModel.PresenceSizePixels.Should().Be(PresenceSettings.DefaultSizePixels);
        fixture.ViewModel.PresenceDotSizePercent.Should().Be(PresenceSettings.DefaultDotSizePercent);
        fixture.ViewModel.PresenceDotDensityPercent.Should().Be(PresenceSettings.DefaultDotDensityPercent);
        fixture.ViewModel.PresenceMovementSpeedPercent.Should()
            .Be(PresenceSettings.DefaultMovementSpeedPercent);
        fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should()
            .Be(PresenceSettings.DefaultSpeechScalingEnabled);
        fixture.ViewModel.PresenceSpeechScaleAmountPercent.Should()
            .Be(PresenceSettings.DefaultSpeechScaleAmountPercent);
        fixture.ViewModel.PresencePosition.Should().BeNull();
        fixture.ViewModel.PresenceSizeDescription.Should().Contain("360 pixels");
        fixture.ViewModel.PresenceDotSizeDescription.Should().Contain("100%");
        fixture.ViewModel.PresenceDotDensityDescription.Should().Contain("100%").And.Contain("150 dots");
        fixture.ViewModel.PresenceMovementSpeedDescription.Should().Contain("100%");
        fixture.ViewModel.PresenceSpeechScalingDescription.Should().Contain("grows and shrinks");
        fixture.ViewModel.PresenceSpeechScaleAmountDescription.Should().Contain("100%");
        fixture.AppearancePreferences.SavedMode.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSizePixels.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceDotSizePercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceDotDensityPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceMovementSpeedPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScaleAmountPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresencePosition.Should().BeNull();
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
        fixture.AppearancePreferences.PresenceTimeoutSeconds = 15;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(15);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_loads_independent_timeouts_without_resaving_or_auditing_writes()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.PresenceTimeoutSeconds = 15;
        fixture.AppearancePreferences.ResponseTimeoutSeconds = 8;
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(15);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(8);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        changedProperties.Should().Contain(nameof(MainViewModel.PresenceTimeoutSeconds))
            .And.Contain(nameof(MainViewModel.ResponseTimeoutSeconds));
        fixture.Audit.Events.Should().NotContain(entry =>
            entry.ActionId == "configuration.presence-timeout"
            || entry.ActionId == "configuration.response-timeout");
    }

    [Fact]
    public async Task InitializeAsync_loads_a_response_timeout_without_changing_the_presence_default()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.ResponseTimeoutSeconds = 18;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(10);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(18);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_loads_saved_presence_settings_without_resaving_them()
    {
        var fixture = new Fixture();
        fixture.AppearancePreferences.PresenceSizePixels = 400;
        fixture.AppearancePreferences.PresenceDotSizePercent = 120;
        fixture.AppearancePreferences.PresenceDotDensityPercent = 125;
        fixture.AppearancePreferences.PresenceMovementSpeedPercent = 125;
        fixture.AppearancePreferences.PresenceSpeechScalingEnabled = false;
        fixture.AppearancePreferences.PresenceSpeechScaleAmountPercent = 150;
        fixture.AppearancePreferences.PresencePosition =
            new PresencePosition(320, -120);

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceSizePixels.Should().Be(400);
        fixture.ViewModel.PresenceDotSizePercent.Should().Be(120);
        fixture.ViewModel.PresenceDotDensityPercent.Should().Be(125);
        fixture.ViewModel.PresenceMovementSpeedPercent.Should().Be(125);
        fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should().BeFalse();
        fixture.ViewModel.PresenceSpeechScaleAmountPercent.Should().Be(150);
        fixture.ViewModel.PresencePosition.Should().Be(
            new PresencePosition(320, -120));
        fixture.AppearancePreferences.SavedPresenceSizePixels.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceDotSizePercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceDotDensityPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceMovementSpeedPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScaleAmountPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresencePosition.Should().BeNull();
    }

    [Fact]
    public async Task Presence_position_changes_persist_notify_and_are_audited()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        var position = new PresencePosition(-640, 240);

        var changed = fixture.ViewModel.SetPresencePosition(
            position,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetPresencePosition(
            position,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.PresencePosition.Should().Be(position);
        fixture.AppearancePreferences.SavedPresencePosition.Should().Be(position);
        changedProperties.Should().ContainSingle()
            .Which.Should().Be(nameof(MainViewModel.PresencePosition));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-position",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Presence_position_rejects_null()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        var action = () => fixture.ViewModel.SetPresencePosition(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Presence_position_save_failure_retains_previous_position(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");

        var changed = fixture.ViewModel.SetPresencePosition(
            new PresencePosition(100, 200));

        changed.Should().BeFalse();
        fixture.ViewModel.PresencePosition.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be(
            "The presence position could not be saved.");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-position",
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
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
            fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);

        var changed = fixture.ViewModel.SetPresenceTimeoutSeconds(
            15,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetPresenceTimeoutSeconds(
            15,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(15);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().Be(15);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
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
        var previousAuditCount = fixture.Audit.Events.Count;
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var action = () => fixture.ViewModel.PresenceTimeoutSeconds = value;

        action.Should().Throw<ArgumentOutOfRangeException>();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.Audit.Events.Should().HaveCount(previousAuditCount);
        changedProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task Response_timeout_changes_persist_before_apply_notify_and_use_the_supplied_audit_initiator()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        fixture.AppearancePreferences.BeforeResponseTimeoutPreferenceSave = () =>
            fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);

        var changed = fixture.ViewModel.SetResponseTimeoutSeconds(
            8,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetResponseTimeoutSeconds(
            8,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(8);
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().Be(8);
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        changedProperties.Should().ContainSingle()
            .Which.Should().Be(nameof(MainViewModel.ResponseTimeoutSeconds));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-timeout",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Timeout_noops_do_not_persist_notify_or_audit()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var previousAuditCount = fixture.Audit.Events.Count;
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        fixture.AppearancePreferences.SaveException = new IOException("Must not save a noop");

        var presenceUnchanged = fixture.ViewModel.SetPresenceTimeoutSeconds(
            PresenceSettings.DefaultTimeoutSeconds);
        var responseUnchanged = fixture.ViewModel.SetResponseTimeoutSeconds(
            ResponseWindowSettings.DefaultTimeoutSeconds);

        presenceUnchanged.Should().BeTrue();
        responseUnchanged.Should().BeTrue();
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        fixture.Audit.Events.Should().HaveCount(previousAuditCount);
        changedProperties.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ResponseWindowSettings.MinimumTimeoutSeconds - 1)]
    [InlineData(ResponseWindowSettings.MaximumTimeoutSeconds + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Response_timeout_change_rejects_invalid_values_without_saving_notifying_or_auditing(int value)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var previousAuditCount = fixture.Audit.Events.Count;
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var action = () => fixture.ViewModel.ResponseTimeoutSeconds = value;

        action.Should().Throw<ArgumentOutOfRangeException>();
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        fixture.Audit.Events.Should().HaveCount(previousAuditCount);
        changedProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task Presence_and_response_timeout_property_setters_persist_independently()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.PresenceTimeoutSeconds = 15;
        fixture.ViewModel.ResponseTimeoutSeconds = 8;
        fixture.ViewModel.PresenceTimeoutSeconds = 20;

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(20);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(8);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().Be(20);
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().Be(8);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-timeout",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
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
        PresenceSetting.Size,
        400,
        nameof(MainViewModel.PresenceSizePixels),
        nameof(MainViewModel.PresenceSizeDescription),
        "configuration.presence-size")]
    [InlineData(
        PresenceSetting.DotSize,
        120,
        nameof(MainViewModel.PresenceDotSizePercent),
        nameof(MainViewModel.PresenceDotSizeDescription),
        "configuration.presence-dot-size")]
    [InlineData(
        PresenceSetting.DotDensity,
        125,
        nameof(MainViewModel.PresenceDotDensityPercent),
        nameof(MainViewModel.PresenceDotDensityDescription),
        "configuration.presence-dot-density")]
    [InlineData(
        PresenceSetting.MovementSpeed,
        125,
        nameof(MainViewModel.PresenceMovementSpeedPercent),
        nameof(MainViewModel.PresenceMovementSpeedDescription),
        "configuration.presence-movement-speed")]
    [InlineData(
        PresenceSetting.SpeechScaleAmount,
        150,
        nameof(MainViewModel.PresenceSpeechScaleAmountPercent),
        nameof(MainViewModel.PresenceSpeechScaleAmountDescription),
        "configuration.presence-speech-scale-amount")]
    public async Task Presence_changes_persist_notify_and_use_the_supplied_audit_initiator(
        PresenceSetting setting,
        int value,
        string propertyName,
        string descriptionPropertyName,
        string actionId)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        var previousValue = GetPresenceSetting(fixture.ViewModel, setting);
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
            GetPresenceSetting(fixture.ViewModel, setting).Should().Be(previousValue);

        var changed = SetPresenceSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = SetPresenceSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        GetPresenceSetting(fixture.ViewModel, setting).Should().Be(value);
        GetSavedPresenceSetting(fixture.AppearancePreferences, setting).Should().Be(value);
        changedProperties.Should().Equal(propertyName, descriptionPropertyName);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            actionId,
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(PresenceSetting.Size, PresenceSettings.MinimumSizePixels - 1)]
    [InlineData(PresenceSetting.Size, PresenceSettings.MaximumSizePixels + 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MinimumDotSizePercent - 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MaximumDotSizePercent + 1)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MinimumDotDensityPercent - 1)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MaximumDotDensityPercent + 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MaximumMovementSpeedPercent + 1)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MinimumSpeechScaleAmountPercent - 1)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MaximumSpeechScaleAmountPercent + 1)]
    public async Task Presence_change_rejects_an_invalid_value(
        PresenceSetting setting,
        int value)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var previousValue = GetPresenceSetting(fixture.ViewModel, setting);
        var previousAuditCount = fixture.Audit.Events.Count;
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var action = () => SetPresenceSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.LocalUser);

        action.Should().Throw<ArgumentOutOfRangeException>();
        GetPresenceSetting(fixture.ViewModel, setting).Should().Be(previousValue);
        GetSavedPresenceSetting(fixture.AppearancePreferences, setting).Should().BeNull();
        changedProperties.Should().BeEmpty();
        fixture.Audit.Events.Should().HaveCount(previousAuditCount);
    }

    [Theory]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MinimumDotDensityPercent)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MaximumDotDensityPercent)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MinimumSpeechScaleAmountPercent)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MaximumSpeechScaleAmountPercent)]
    public async Task New_presence_settings_accept_boundary_values(
        PresenceSetting setting,
        int value)
    {
        var fixture = await Fixture.CreateInitializedAsync();

        SetPresenceSetting(
            fixture.ViewModel,
            setting,
            value,
            SecurityAuditInitiator.LocalUser).Should().BeTrue();

        GetPresenceSetting(fixture.ViewModel, setting).Should().Be(value);
        GetSavedPresenceSetting(fixture.AppearancePreferences, setting).Should().Be(value);
    }

    [Fact]
    public async Task Presence_speech_scaling_changes_persist_notify_and_are_audited()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
            fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should().BeTrue();

        var changed = fixture.ViewModel.SetPresenceSpeechScalingEnabled(
            false,
            SecurityAuditInitiator.VoiceCommand);
        var unchanged = fixture.ViewModel.SetPresenceSpeechScalingEnabled(
            false,
            SecurityAuditInitiator.VoiceCommand);

        changed.Should().BeTrue();
        unchanged.Should().BeTrue();
        fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should().BeFalse();
        fixture.ViewModel.PresenceSpeechScalingDescription.Should().Contain("normal size");
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeFalse();
        changedProperties.Should().Equal(
            nameof(MainViewModel.IsPresenceSpeechScalingEnabled),
            nameof(MainViewModel.PresenceSpeechScalingDescription));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-speech-scaling",
            SecurityAuditInitiator.VoiceCommand,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task New_presence_bound_properties_persist_changes_and_retain_amount_when_disabled()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.PresenceDotDensityPercent = 125;
        fixture.ViewModel.PresenceSpeechScaleAmountPercent = 150;
        fixture.ViewModel.IsPresenceSpeechScalingEnabled = false;

        fixture.AppearancePreferences.SavedPresenceDotDensityPercent.Should().Be(125);
        fixture.AppearancePreferences.SavedPresenceSpeechScaleAmountPercent.Should().Be(150);
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeFalse();
        fixture.ViewModel.PresenceSpeechScaleAmountPercent.Should().Be(150);

        fixture.ViewModel.IsPresenceSpeechScalingEnabled = true;

        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeTrue();
        fixture.ViewModel.PresenceSpeechScaleAmountPercent.Should().Be(150);
    }

    [Theory]
    [InlineData(true, "access-denied")]
    [InlineData(false, "io-error")]
    public async Task Presence_speech_scaling_save_failure_retains_previous_value_and_is_audited(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = fixture.ViewModel.SetPresenceSpeechScalingEnabled(false);

        changed.Should().BeFalse();
        fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should().BeTrue();
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be(
            "The presence speech scaling could not be saved.");
        changedProperties.Should().Contain(nameof(MainViewModel.IsPresenceSpeechScalingEnabled));
        changedProperties.Should().NotContain(nameof(MainViewModel.PresenceSpeechScalingDescription));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.presence-speech-scaling",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            reasonCode);
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

        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = fixture.ViewModel.SetPresenceTimeoutSeconds(15);

        changed.Should().BeFalse();
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("The presence timeout could not be saved.");
        changedProperties.Should().Contain(nameof(MainViewModel.PresenceTimeoutSeconds))
            .And.NotContain(nameof(MainViewModel.ResponseTimeoutSeconds));
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
    public async Task Response_timeout_save_failure_retains_both_values_notifies_and_is_audited(
        bool accessDenied,
        string reasonCode)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = fixture.ViewModel.SetResponseTimeoutSeconds(8);

        changed.Should().BeFalse();
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(PresenceSettings.DefaultTimeoutSeconds);
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("The response timeout could not be saved.");
        changedProperties.Should().Contain(nameof(MainViewModel.ResponseTimeoutSeconds))
            .And.NotContain(nameof(MainViewModel.PresenceTimeoutSeconds));
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.response-timeout",
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
        PresenceSetting.Size,
        true,
        "access-denied",
        "configuration.presence-size",
        "The presence size could not be saved.")]
    [InlineData(
        PresenceSetting.Size,
        false,
        "io-error",
        "configuration.presence-size",
        "The presence size could not be saved.")]
    [InlineData(
        PresenceSetting.DotSize,
        true,
        "access-denied",
        "configuration.presence-dot-size",
        "The presence dot size could not be saved.")]
    [InlineData(
        PresenceSetting.DotSize,
        false,
        "io-error",
        "configuration.presence-dot-size",
        "The presence dot size could not be saved.")]
    [InlineData(
        PresenceSetting.DotDensity,
        true,
        "access-denied",
        "configuration.presence-dot-density",
        "The presence dot density could not be saved.")]
    [InlineData(
        PresenceSetting.DotDensity,
        false,
        "io-error",
        "configuration.presence-dot-density",
        "The presence dot density could not be saved.")]
    [InlineData(
        PresenceSetting.MovementSpeed,
        true,
        "access-denied",
        "configuration.presence-movement-speed",
        "The presence movement speed could not be saved.")]
    [InlineData(
        PresenceSetting.MovementSpeed,
        false,
        "io-error",
        "configuration.presence-movement-speed",
        "The presence movement speed could not be saved.")]
    [InlineData(
        PresenceSetting.SpeechScaleAmount,
        true,
        "access-denied",
        "configuration.presence-speech-scale-amount",
        "The presence speech scale amount could not be saved.")]
    [InlineData(
        PresenceSetting.SpeechScaleAmount,
        false,
        "io-error",
        "configuration.presence-speech-scale-amount",
        "The presence speech scale amount could not be saved.")]
    public async Task Presence_save_failure_retains_the_previous_value_and_is_audited(
        PresenceSetting setting,
        bool accessDenied,
        string reasonCode,
        string actionId,
        string expectedTitle)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.AppearancePreferences.SaveException = accessDenied
            ? new UnauthorizedAccessException("denied")
            : new IOException("disk");
        var previousValue = GetPresenceSetting(fixture.ViewModel, setting);
        var changedProperties = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
            changedProperties.Add(eventArgs.PropertyName);

        var changed = SetPresenceSetting(
            fixture.ViewModel,
            setting,
            previousValue + 20,
            SecurityAuditInitiator.LocalUser);

        changed.Should().BeFalse();
        GetPresenceSetting(fixture.ViewModel, setting).Should().Be(previousValue);
        GetSavedPresenceSetting(fixture.AppearancePreferences, setting).Should().BeNull();
        var propertyName = setting switch
        {
            PresenceSetting.Size => nameof(MainViewModel.PresenceSizePixels),
            PresenceSetting.DotSize => nameof(MainViewModel.PresenceDotSizePercent),
            PresenceSetting.DotDensity => nameof(MainViewModel.PresenceDotDensityPercent),
            PresenceSetting.MovementSpeed => nameof(MainViewModel.PresenceMovementSpeedPercent),
            PresenceSetting.SpeechScaleAmount => nameof(MainViewModel.PresenceSpeechScaleAmountPercent),
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };
        changedProperties.Should().Contain(propertyName);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializeAsync_surfaces_invalid_saved_timeouts_without_writes(bool presence)
    {
        var fixture = new Fixture();
        var exception = new InvalidDataException("invalid saved timeout");
        if (presence)
        {
            fixture.AppearancePreferences.PresenceTimeoutLoadException = exception;
        }
        else
        {
            fixture.AppearancePreferences.ResponseTimeoutLoadException = exception;
        }

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceTimeoutSeconds.Should().Be(10);
        fixture.ViewModel.ResponseTimeoutSeconds.Should().Be(5);
        fixture.ViewModel.ResponseTitle.Should().Be("A saved setting is invalid.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.AppearancePreferences.SavedPresenceTimeoutSeconds.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseTimeoutSeconds.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(entry =>
            entry.ActionId == "configuration.presence-timeout"
            || entry.ActionId == "configuration.response-timeout");
    }

    [Theory]
    [InlineData("density")]
    [InlineData("speech-scaling")]
    [InlineData("speech-scale-amount")]
    public async Task InitializeAsync_surfaces_an_invalid_saved_presence_setting(string setting)
    {
        var fixture = new Fixture();
        var exception = new InvalidDataException("invalid saved presence setting");
        switch (setting)
        {
            case "density":
                fixture.AppearancePreferences.DotDensityLoadException = exception;
                break;
            case "speech-scaling":
                fixture.AppearancePreferences.SpeechScalingLoadException = exception;
                break;
            case "speech-scale-amount":
                fixture.AppearancePreferences.SpeechScaleAmountLoadException = exception;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting));
        }

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PresenceDotDensityPercent.Should().Be(PresenceSettings.DefaultDotDensityPercent);
        fixture.ViewModel.IsPresenceSpeechScalingEnabled.Should().BeTrue();
        fixture.ViewModel.PresenceSpeechScaleAmountPercent.Should()
            .Be(PresenceSettings.DefaultSpeechScaleAmountPercent);
        fixture.ViewModel.ResponseTitle.Should().Be("A saved setting is invalid.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.AppearancePreferences.SavedPresenceDotDensityPercent.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScalingEnabled.Should().BeNull();
        fixture.AppearancePreferences.SavedPresenceSpeechScaleAmountPercent.Should().BeNull();
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
        fixture.ViewModel.SettingsWindowTitle.Should().Be("Nova settings - 1.2.3");
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
    public async Task Applying_a_name_retires_capture_and_new_PTT_uses_the_new_grammar()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.AssistantNameInput = "Nova";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(1);
        fixture.Voice.StartCalls.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeFalse();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StartCalls.Should().Be(2);
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
    public async Task Selecting_System_clears_microphone_but_output_remains_closed_without_admission()
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
        fixture.AudioPreferences.OutputDeviceId.Should().Be("output-saved");
        fixture.AudioPreferences.ClearedMicrophoneCount.Should().Be(1);
        fixture.AudioPreferences.ClearedOutputDeviceCount.Should().Be(0);
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be("output-saved");
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

        fixture.ViewModel.SelectedMicrophone!.Id.Should().Be("microphone-removed");
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
        fixture.TextToSpeech.Providers = [CreateWindowsProvider() with { DefaultVoiceId = "male" }];
        fixture.TextToSpeech.Voices =
        [
            new SpeechVoice(
                "male",
                "Male voice",
                CultureInfo.CurrentUICulture.Name,
                SpeechVoiceGender.Male),
        ];

        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedVoice.Should().NotBeNull();
        fixture.ViewModel.SelectedVoice!.Id.Should().Be("male");
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
    public async Task InitializeAsync_reports_missing_saved_voice_without_substitution_until_explicit_reset()
    {
        var fixture = new Fixture();
        fixture.Preferences.VoiceId = "removed";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("unavailable");
        fixture.Preferences.VoiceId.Should().Be("removed");
        await fixture.ViewModel.ResetSpeechVoiceCommand.ExecuteAsync();
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
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
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("unavailable");
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
    public async Task InitializeAsync_requires_recovery_when_the_saved_provider_is_unknown()
    {
        var fixture = new Fixture();
        fixture.Preferences.ProviderId = "removed-provider";
        fixture.Preferences.VoiceId = "removed-voice";

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.SelectedInstalledSpeechProvider.Should().BeNull();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("invalid");
    }

    [Fact]
    public async Task Refresh_rejects_changed_unknown_saved_state_instead_of_retaining_unreported_runtime_values()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Preferences.ProviderId = "removed-provider";
        fixture.Preferences.VoiceId = "removed-voice";

        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.SelectedInstalledSpeechProvider.Should().BeNull();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("invalid");
    }

    [Fact]
    public async Task InitializeAsync_requires_explicit_selection_when_the_default_provider_is_absent()
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
        fixture.ViewModel.SelectedInstalledSpeechProvider.Should().BeNull();
        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("unavailable");
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

        fixture.ViewModel.SelectedInstalledSpeechProvider =
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

        fixture.ViewModel.SelectedInstalledSpeechProvider =
            fixture.ViewModel.SpeechProviders.Single(
                provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal));

        fixture.ViewModel.ResponseTitle.Should().Be(
            "The speech setting was not changed.");
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
    public async Task Downloading_Kokoro_exposes_ready_choices_without_silently_changing_output()
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
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
        fixture.Preferences.SavedProviderId.Should().BeNull();
        fixture.Preferences.SavedVoiceId.Should().BeNull();
        fixture.ViewModel.SpeechProviderOperationProgress.Should().Be(100);
        fixture.ViewModel.IsSpeechProviderOperationActive.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Kokoro is ready.");
        fixture.TextToSpeech.SpokenText.Should().Contain("Kokoro is ready.");
        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("female");
        fixture.ViewModel.CanRemoveSpeechProvider.Should().BeTrue();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "speech-provider.install",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task First_run_offers_missing_optional_speech_once_without_queueing_or_downloading_it()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.InitializeAsync();

        var offer = fixture.ViewModel.GetOptionalSpeechProviderOffer();

        offer.Should().NotBeNull();
        offer!.IsRecovery.Should().BeFalse();
        offer.ProviderId.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.SetupTasks.Should().NotContain(task =>
            string.Equals(task.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal));
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);

        fixture.ViewModel.AcknowledgeOptionalSpeechProviderOffer(offer, openSettings: false);
        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();
        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(SpeechProviderIds.Windows);
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_previously_selected_speech_provider_is_offered_once_per_loss_episode()
    {
        var fixture = new Fixture();
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();

        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        var offer = fixture.ViewModel.GetOptionalSpeechProviderOffer();
        offer.Should().NotBeNull();
        offer!.IsRecovery.Should().BeTrue();
        fixture.ViewModel.AcknowledgeOptionalSpeechProviderOffer(offer, openSettings: false);
        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();

        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: true),
        ];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();

        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().NotBeNull();
    }

    [Fact]
    public async Task Reviewing_optional_speech_selects_it_in_settings_without_starting_a_download()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.InitializeAsync();
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        var offer = fixture.ViewModel.GetOptionalSpeechProviderOffer();

        fixture.ViewModel.AcknowledgeOptionalSpeechProviderOffer(offer!, openSettings: true);

        settingsRequests.Should().Be(1);
        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(SpeechProviderIds.Kokoro);
        fixture.ViewModel.CanDownloadSpeechProvider.Should().BeTrue();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
        fixture.ViewModel.SetupTasks.Should().NotContain(task =>
            string.Equals(task.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal)
            || string.Equals(task.Id, "windows.tts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_removed_optional_provider_offers_recovery_with_a_safe_fallback_name()
    {
        var fixture = new Fixture();
        fixture.Preferences.ProviderId = SpeechProviderIds.Kokoro;
        await fixture.ViewModel.InitializeAsync();

        var offer = fixture.ViewModel.GetOptionalSpeechProviderOffer();

        offer.Should().NotBeNull();
        offer!.IsRecovery.Should().BeTrue();
        offer.Title.Should().Contain("selected speech provider");
        fixture.ViewModel.AcknowledgeOptionalSpeechProviderOffer(offer, openSettings: true);
        fixture.ViewModel.SelectedSpeechProvider?.Id.Should().Be(SpeechProviderIds.Windows);
        fixture.SpeechOffers.State.MissingProviderNotified.Should().Be(SpeechProviderIds.Kokoro);
    }

    [Fact]
    public async Task Installed_optional_provider_clears_the_initial_offer_without_installation()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers = [CreateWindowsProvider(), CreateKokoroProvider(isInstalled: true)];
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();

        fixture.SpeechOffers.State.InitialOfferHandled.Should().BeTrue();
        fixture.TextToSpeech.InstallProviderCalls.Should().Be(0);
    }

    [Fact]
    public async Task Speech_offer_storage_errors_are_reported_without_claiming_a_response()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.SpeechOffers.Failure = new IOException("preferences unavailable");

        fixture.ViewModel.GetOptionalSpeechProviderOffer().Should().BeNull();
        fixture.ViewModel.ResponseTitle.Should().Be("Speech offer settings could not be loaded.");
        var offer = new OptionalSpeechProviderOffer("Optional speech", "Review?", SpeechProviderIds.Kokoro, false);
        fixture.ViewModel.AcknowledgeOptionalSpeechProviderOffer(offer, openSettings: true);
        fixture.ViewModel.ResponseTitle.Should().Be("Speech offer response could not be saved.");
        fixture.SpeechOffers.State.InitialOfferHandled.Should().BeFalse();
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

        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.SpokenVoice?.Id.Should().Be("female");
    }

    [Fact]
    public async Task Download_failure_does_not_substitute_an_unadmitted_provider()
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

        fixture.TextToSpeech.SpokenVoice.Should().BeNull();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
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
        fixture.ViewModel.VoiceAvailabilityMessage.Should().Contain("unavailable");
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
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
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

    [Fact]
    public async Task Unexpected_response_playback_failure_is_visible_without_escaping()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new IOException("unexpected playback failure");

        Func<Task> action = () => fixture.RunAsync("what power action is pending");

        await action.Should().NotThrowAsync();
        fixture.ViewModel.SelectedVoice.Should().NotBeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseBody.Should().Be("Power execution is intentionally disabled in this bootstrap proof.");
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Speech playback failed");
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

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
        await LoadSavedOutputAsync(fixture, "unavailable");
        fixture.TextToSpeech.ClearSpokenResponse();

        await fixture.RunAsync("unsupported");

        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("no longer available");
    }

    [Fact]
    public async Task Removed_audio_output_is_not_silently_replaced_during_refresh()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
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
        fixture.TextToSpeech.OutputDevices = [];
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.TextToSpeech.OutputDevices =
            [new AudioOutputDevice("0", "Muted", IsMuted: true)];
        await fixture.RunAsync("what power action is pending");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();

        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output")];
        await fixture.RunAsync("what power action is pending");
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

        await fixture.RunAsync("what power action is pending");

        fixture.TextToSpeech.SpokenText.Should().Contain("No power action is pending.");

        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("Kora, what power action is pending");

        fixture.TextToSpeech.SpokenText.Should().Contain("No power action is pending.");
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
        var fixture = new Fixture();
        fixture.CallPreferences.Settings = new(false, true);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;

        fixture.CallState.SetState(CallState.Suspected);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeFalse();
        fixture.ViewModel.IsCallDetected.Should().BeTrue();
        fixture.ViewModel.IsCallVisualOverrideActive.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue();
        fixture.ViewModel.CallVisualOverrideButtonText.Should().Be("Show visual text during calls");
        fixture.CallPreferences.SavedSettings.Should().BeNull();

        await fixture.RunAsync("what power action is pending");

        fixture.TextToSpeech.SpokenText.Should().NotBeNull();
    }

    [Fact]
    public async Task Voice_activation_remains_armed_without_capture_when_a_call_is_detected_by_default()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();

        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;

        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeFalse();
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

        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("exact trusted review");
    }

    [Fact]
    public async Task Enabling_visual_override_during_a_call_stops_current_speech()
    {
        var fixture = new Fixture();
        fixture.CallPreferences.Settings = new(false, true);
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseTask = fixture.RunAsync("what power action is pending");
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

        await fixture.ViewModel.ToggleCallVoiceActivationCommand.ExecuteAsync();

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

        fixture.ViewModel.ShowVisualTextDuringCalls.Should().BeTrue();
        fixture.ViewModel.AllowVoiceActivationDuringCalls.Should().BeFalse();
        fixture.ViewModel.CallVisualOverrideStatus.Should().StartWith("On");
        fixture.ViewModel.CallVoiceActivationStatus.Should().StartWith("Off");
        changedProperties.Should().Contain(nameof(MainViewModel.AllowVoiceActivationDuringCalls));
        changedProperties.Should().Contain(nameof(MainViewModel.CallVoiceActivationStatus));
        fixture.CallPreferences.SavedSettings.Should().Be(new CallAwareSettings(true, false));
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
    public async Task Open_microphone_privacy_settings_command_routes_unexpected_failures()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Process.OpenMicrophoneSettingsException = new IOException("Process launch failed.");

        fixture.ViewModel.OpenMicrophonePrivacySettingsCommand.Execute(null);

        fixture.ViewModel.ResponseTitle.Should().Be("The command failed.");
        fixture.ViewModel.ResponseBody.Should().Contain("Process launch failed.");
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

        await fixture.RaiseActivatedTranscriptAsync("Kora, what power action is pending", 0.9f);

        fixture.TextToSpeech.SpokenText.Should().Contain("No power action is pending.");
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

        await fixture.RunAsync("what power action is pending");

        fixture.ViewModel.SelectedVoice.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseOutputStatus.Should().Contain(expectedTitle);
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

        await fixture.RunAsync("what power action is pending");

        fixture.ViewModel.SelectedVoice.Should().NotBeNull();
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Audio output is unavailable");
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

        await fixture.RunAsync("what power action is pending");

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseBody.Should().Contain("Power execution is intentionally disabled");
        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
    }

    [Fact]
    public async Task Runtime_mute_marks_an_explicit_output_as_muted()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted,
            "endpoint muted");

        await fixture.RunAsync("what power action is pending");

        fixture.ViewModel.SelectedOutputDevice?.Id.Should().Be("0");
        fixture.ViewModel.SelectedOutputDevice?.IsMuted.Should().BeTrue();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Visual text is forced");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Muted_voice_only_response_uses_configured_fallback_without_losing_content(
        bool fallbackEnabled,
        bool explicitOutput)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = fallbackEnabled;
        if (explicitOutput)
        {
            await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        }
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.WindowActions.Clear();

        await fixture.RunAsync("Kora, what can you do?");

        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
        fixture.ViewModel.ResponseBody.Should().NotBeNullOrWhiteSpace();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.DefaultResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be(explicitOutput ? "0" : SystemAudioDevices.Output.Id);
        fixture.WindowActions.Should().Equal(WindowAction.Show);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Runtime_mute_race_preserves_the_response_and_respects_the_fallback_option(
        bool fallbackEnabled,
        bool explicitOutput)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = fallbackEnabled;
        if (explicitOutput)
        {
            await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        }
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted, "endpoint muted");
        fixture.WindowActions.Clear();

        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseBody.Should().NotBeNullOrWhiteSpace();
        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.SelectedOutputDevice.Should().NotBeNull();
        fixture.WindowActions.Should().Equal(WindowAction.Show);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unmuting_output_restores_voice_only_responses_without_a_manual_refresh(bool explicitOutput)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.QueueResponseMode = ResponseOutputMode.VoiceOnly;
        if (explicitOutput)
        {
            await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        }
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        await fixture.RunAsync("Kora, what power action is pending");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();

        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output")];
        fixture.WindowActions.Clear();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeTrue();
        fixture.TextToSpeech.SpokenText.Should().Contain("No power action is pending.");
        fixture.ViewModel.EffectiveResponseMode.Should().Be(ResponseOutputMode.VoiceOnly);
        fixture.WindowActions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InitializeAsync_loads_the_muted_output_fallback_without_saving(bool? saved, bool expected)
    {
        var fixture = new Fixture();
        fixture.OutputPreferences.MutedOutputVisualFallback = saved;

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.FallbackToVisualWhenOutputMuted.Should().Be(expected);
        fixture.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeNull();
    }

    [Fact]
    public async Task Changing_muted_output_fallback_saves_and_updates_the_current_response()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        await fixture.RunAsync("Kora, what can you do?");
        fixture.WindowActions.Clear();
        fixture.Audit.Events.Clear();
        var changes = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        fixture.ViewModel.FallbackToVisualWhenOutputMuted = true;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = true;

        fixture.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Visual text is forced");
        changes.Should().Contain(nameof(MainViewModel.FallbackToVisualWhenOutputMuted));
        changes.Should().Contain(nameof(MainViewModel.IsVisualResponseVisible));
        fixture.WindowActions.Should().BeEmpty();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.muted-output-visual-fallback",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);

        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.OutputPreferences.SavedMutedOutputVisualFallback.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.OutputDeviceAvailabilityMessage.Should().Contain("Visual text is forced");
    }

    [Theory]
    [MemberData(nameof(OutputModeSaveFailures))]
    public async Task Muted_output_fallback_save_failure_preserves_the_setting_and_reports_failure(Exception exception)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.OutputPreferences.SaveException = exception;

        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;

        fixture.ViewModel.FallbackToVisualWhenOutputMuted.Should().BeTrue();
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The muted-output visual fallback could not be saved.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ConfigurationWrite,
            "configuration.muted-output-visual-fallback",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Failed,
            exception is UnauthorizedAccessException ? "access-denied" : "io-error");
    }

    [Fact]
    public async Task Disabling_muted_output_fallback_does_not_hide_missing_output_or_missing_voice()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.OutputDevices = [];
        await fixture.RunAsync("Kora, what can you do?");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();

        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        fixture.ViewModel.SelectedVoice = null;
        await fixture.RunAsync("Kora, what can you do?");

        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Disabling_muted_output_fallback_does_not_hide_playback_failures()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.TaskResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.SpeakException = new InvalidOperationException("playback failed");
        fixture.WindowActions.Clear();

        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.ResponseOutputStatus.Should().Contain("Text-to-speech is unavailable");
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().Equal(WindowAction.Show);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabling_muted_output_fallback_does_not_reveal_a_response_in_a_locked_session(bool initializing)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        await fixture.RunAsync("Kora, what can you do?");
        fixture.WindowActions.Clear();
        fixture.Session.IsUnlocked = false;

        if (initializing)
        {
            fixture.OutputPreferences.MutedOutputVisualFallback = true;
            await fixture.ViewModel.InitializeAsync();
        }
        else
        {
            fixture.ViewModel.FallbackToVisualWhenOutputMuted = true;
        }

        fixture.ViewModel.FallbackToVisualWhenOutputMuted.Should().Be(initializing);
        fixture.WindowActions.Should().NotContain(WindowAction.Show);
    }

    [Fact]
    public async Task Reinitializing_with_enabled_muted_output_fallback_does_not_request_a_window()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        await fixture.RunAsync("Kora, what can you do?");
        fixture.OutputPreferences.MutedOutputVisualFallback = true;
        fixture.WindowActions.Clear();

        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.FallbackToVisualWhenOutputMuted.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Enabling_muted_output_fallback_is_safe_without_a_window_subscriber()
    {
        var fixture = await Fixture.CreateInitializedAsync(subscribeToWindowActions: false);
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.FallbackToVisualWhenOutputMuted = false;
        fixture.TextToSpeech.OutputDevices = [new AudioOutputDevice("0", "Default output", IsMuted: true)];
        await fixture.RunAsync("Kora, what can you do?");

        fixture.ViewModel.FallbackToVisualWhenOutputMuted = true;

        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Voice_preview_reports_runtime_mute_instead_of_retaining_an_ordinary_response(
        bool explicitOutput, bool removedDuringPlayback)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        if (explicitOutput)
        {
            await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        }
        if (removedDuringPlayback)
        {
            fixture.TextToSpeech.BeforeSpeak = () => fixture.ViewModel.OutputDevices.Clear();
        }
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted, "endpoint muted during preview");
        fixture.WindowActions.Clear();

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is muted.");
        fixture.ViewModel.ResponseBody.Should().Be("endpoint muted during preview");
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.SelectedOutputDevice!.Id.Should().Be(explicitOutput ? "0" : SystemAudioDevices.Output.Id);
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().Equal(WindowAction.Show);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Runtime_mute_fallback_respects_session_privacy_and_absent_window_subscribers(
        bool explicitOutput, bool lockedDuringPlayback)
    {
        var fixture = await Fixture.CreateInitializedAsync(subscribeToWindowActions: false);
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        if (explicitOutput)
        {
            await LoadSavedOutputAsync(fixture, fixture.TextToSpeech.OutputDevices[0].Id);
        }
        if (lockedDuringPlayback)
        {
            fixture.ViewModel.WindowActionRequested += (_, action) => fixture.WindowActions.Add(action);
            fixture.TextToSpeech.BeforeSpeak = () => fixture.Session.IsUnlocked = false;
        }
        fixture.TextToSpeech.SpeakException = new AudioOutputDeviceUnavailableException(
            AudioOutputFailureReason.Muted, "endpoint muted");
        fixture.WindowActions.Clear();

        await fixture.RunAsync("Kora, what power action is pending");

        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Output_enumeration_failure_before_a_response_is_reported_without_overwriting_the_error()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.TextToSpeech.OutputEnumerationException = new IOException("render endpoints unavailable");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.WindowActions.Clear();

        await fixture.RunAsync("Kora, what can you do?");

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("Audio output is unavailable.");
        fixture.ViewModel.ResponseBody.Should().Be("render endpoints unavailable");
        fixture.TextToSpeech.SpokenText.Should().NotContain("Built-in commands are ready.");
        fixture.WindowActions.Should().Equal(WindowAction.Show);
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
        fixture.TextToSpeech.Voices = [.. fixture.TextToSpeech.Voices, alternative];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();

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
        fixture.TextToSpeech.Voices = [.. fixture.TextToSpeech.Voices, alternative];
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.Preferences.SaveException = exception;

        fixture.ViewModel.SelectedVoice = alternative;

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be("The speech setting was not changed.");
        fixture.ViewModel.ResponseBody.Should().Be(exception.Message);
        fixture.ViewModel.SelectedVoice?.Id.Should().Be("female");
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
    public async Task Speaker_override_cannot_be_changed_without_session_and_choice_admission()
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

        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "configuration.audio-output");
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
        var fixture = new Fixture(enableOutputConfiguration: !isMicrophone);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
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
            fixture.TextToSpeech.OutputDevices = [output];
            await fixture.ViewModel.RefreshOutputDevicesCommand.ExecuteAsync();
            fixture.AudioPreferences.OutputDeviceSaveException = exception;
            fixture.ViewModel.SelectedOutputChoice = fixture.ViewModel.OutputDeviceChoices.Single(item => string.Equals(item.Id, output.Id, StringComparison.Ordinal));
            await fixture.ViewModel.SaveOutputDeviceCommand.ExecuteAsync();
        }

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        if (!isMicrophone)
        {
            fixture.ViewModel.ResponseTitle.Should().Be("Output preference not confirmed.");
            fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        }
        else { fixture.ViewModel.ResponseTitle.Should().Be("The microphone preference could not be saved."); }
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
        fixture.Voice.StartCalls.Should().Be(0);
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.StartedMicrophone.Should().Be(fixture.ViewModel.SelectedMicrophone);
        var commandPhrases = fixture.Catalog.GetCommands().SelectMany(command => command.AllPhrases)
            .Concat(Kora.Core.Context.ClipboardCommand.FixedPhrases)
            .Concat(SessionCommand.DiscoveryPhrases)
            .Concat(AssistantNameCommand.DiscoveryPhrases)
            .Concat(InputDeviceCommand.FixedPhrases)
            .Concat(OutputDeviceCommand.FixedPhrases)
            .Concat(PlaybackVolumeCommand.FixedPhrases);
        fixture.Voice.StartedPhrases.Should().BeEquivalentTo(
            commandPhrases.SelectMany(phrase => new[] { phrase, $"Kora {phrase}" })
                .Concat(ModelApprovalSpeech.GetPhrases("Kora"))
                .Distinct(StringComparer.OrdinalIgnoreCase));
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
    public async Task Preview_voice_is_disabled_when_the_selected_provider_has_no_selected_voice()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.Providers =
        [
            CreateWindowsProvider(),
            CreateKokoroProvider(isInstalled: false),
        ];
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSpeechProvider = fixture.ViewModel.SpeechProviders.Single(provider =>
            string.Equals(provider.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal));
        fixture.ViewModel.SelectedVoice = null;

        fixture.ViewModel.IsSpeechOutputAvailable.Should().BeFalse();
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeFalse();

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_voice_keeps_PTT_armed_without_ambient_recording()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.ViewModel.PreviewVoiceCommand.CanExecute(null).Should().BeTrue();

        await fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();

        fixture.Voice.StopCalls.Should().Be(0);
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().Be("Hello, I'm Kora.");
    }

    [Fact]
    public async Task Explicit_PTT_interrupts_active_preview_before_command_capture()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var settingsRequests = 0;
        fixture.ViewModel.SettingsRequested += (_, _) => settingsRequests++;
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var previewTask = fixture.ViewModel.PreviewVoiceCommand.ExecuteAsync();
        await fixture.TextToSpeech.SpeakStarted.Task;
        await fixture.RaiseActivatedTranscriptAsync("Kora, open settings", 0.9f);
        await previewTask;

        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
        settingsRequests.Should().Be(1);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
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

        var responseTask = fixture.RunAsync("Kora, what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        await fixture.Voice.RaiseTranscriptAsync(
            "Kora, open documentation",
            0.95f);

        fixture.TextToSpeech.StopCalls.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await responseTask;
    }

    [Fact]
    public async Task Cancel_task_button_state_includes_built_in_response_speech()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var responseTask = fixture.RunAsync("Kora, what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.ViewModel.IsCancelTaskVisible.Should().BeTrue();

        await fixture.ViewModel.CancelCurrentTaskAsync();
        await responseTask;

        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsCancelTaskVisible.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Speech is stopped.");
    }

    [Theory]
    [InlineData(typeof(ArgumentOutOfRangeException), "The selected speech voice is unavailable.")]
    [InlineData(typeof(AudioOutputDeviceUnavailableException), "The selected audio output is unavailable.")]
    [InlineData(typeof(InvalidOperationException), "Text-to-speech is unavailable.")]
    [InlineData(typeof(IOException), "Voice preview failed.")]
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
    public async Task Stop_speaking_failure_is_visible_without_escaping()
    {
        var fixture = new Fixture();
        fixture.TextToSpeech.StopException = new IOException("playback cannot stop");

        Func<Task> action = () => fixture.RunAsync("Kora, stop speaking");

        await action.Should().NotThrowAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
        fixture.ViewModel.ResponseBody.Should().Contain("playback cannot stop");
    }

    [Fact]
    public async Task Other_commands_are_disabled_while_microphone_start_is_in_progress()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync();
        var start = fixture.ViewModel.BeginPushToTalkCommand.ExecuteAsync();

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
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseTitle.Should().Be(expectedTitle);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    public static TheoryData<Exception, string> StartFailures => new()
    {
        { new ArgumentOutOfRangeException("microphone", "gone"), "The selected microphone is unavailable." },
        { new InvalidOperationException("recognizer missing"), "Windows speech recognition is unavailable." },
        { new NotSupportedException("unexpected adapter failure"), "Voice capture failed." },
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

    [Fact]
    public async Task Unmatched_typed_request_uses_verified_local_model_without_executing_actions()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("Kora, why is the sky blue?");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().ContainSingle().Which.Should().Be("why is the sky blue?");
        fixture.ViewModel.ResponseTitle.Should().Be("Local model response");
        fixture.ViewModel.ResponseBody.Should().Contain("A local answer.");
        fixture.ViewModel.SetupTasks.Should().Contain(task =>
            task.Id == "local.reasoning" && task.State == SetupTaskState.Completed);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Typed_slash_command_selects_artifact_without_bypassing_the_model_action_gate()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("/lock");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().ContainSingle().Which
            .Should().Be("Run the selected skill.");
        fixture.Reasoner.LastArtifact.Should().NotBeNull();
        fixture.Reasoner.LastArtifact!.Id.Should().Be("kora.session.lock");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Activated_voice_artifact_command_uses_the_same_artifact_route()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RaiseActivatedTranscriptAsync("Kora, use lock to lock this session", 0.91f);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().ContainSingle().Which
            .Should().Be("lock this session");
        fixture.Reasoner.LastArtifact!.Id.Should().Be("kora.session.lock");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Artifact_selection_is_retained_across_a_clarification_answer()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which target?", ["Current", "Other"]);
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("/lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Question = null;
        await fixture.ViewModel.SelectModelQuestionChoiceAsync(
            fixture.ViewModel.ModelQuestionChoices[0]);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().HaveCount(2);
        fixture.Reasoner.LastArtifact!.Id.Should().Be("kora.session.lock");
    }

    [Fact]
    public async Task Unknown_slash_command_fails_closed_without_invoking_the_model()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("/not-registered");

        fixture.ViewModel.ResponseTitle.Should().Be("Artifact command not found.");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Slash_command_dropdown_filters_and_applies_available_artifacts()
    {
        var fixture = new Fixture();

        fixture.ViewModel.CommandText = "/";

        fixture.ViewModel.IsArtifactCommandDropdownVisible.Should().BeTrue();
        fixture.ViewModel.ArtifactCommandOptions.Should().ContainSingle();
        var option = fixture.ViewModel.ArtifactCommandOptions[0];
        option.Command.Should().Be("/lock");
        option.Source.Should().Be("bundled");

        fixture.ViewModel.ApplyArtifactCommandOption(option);

        fixture.ViewModel.CommandText.Should().Be("/lock ");
        fixture.ViewModel.IsArtifactCommandDropdownVisible.Should().BeFalse();

        fixture.ViewModel.CommandText = "/";
        fixture.ViewModel.DismissArtifactCommandOptions();
        fixture.ViewModel.IsArtifactCommandDropdownVisible.Should().BeFalse();

        fixture.ViewModel.ApplyArtifactCommandOption(option);
        fixture.ViewModel.CommandText.Should().Be("/");
    }

    [Fact]
    public void Slash_command_dropdown_supports_kind_qualification_and_dismissal()
    {
        var artifacts = new[]
        {
            new ArtifactDefinition(
                "kora.skill.test", ArtifactKind.Skill, "Test skill", "Skill.",
                "test-skill", ["test skill"], "bundled", "1.0.0", new string('1', 64), "Skill body."),
            new ArtifactDefinition(
                "kora.prompt.test", ArtifactKind.Prompt, "Test prompt", "Prompt.",
                "test-prompt", ["test prompt"], "bundled", "1.0.0", new string('2', 64), "Prompt body."),
        };

        MainViewModel.FilterArtifactCommandOptions("/prompt test", artifacts)
            .Should().ContainSingle().Which.Command.Should().Be("/test-prompt");
        MainViewModel.FilterArtifactCommandOptions("/skill test", artifacts)
            .Should().ContainSingle().Which.Command.Should().Be("/test-skill");
        MainViewModel.FilterArtifactCommandOptions("not a command", artifacts).Should().BeEmpty();
    }

    [Fact]
    public async Task Revoking_a_session_grant_publishes_the_updated_grant_document()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        string? updatedDocument = null;
        fixture.ViewModel.GrantDocumentChanged += (_, document) => updatedDocument = document;

        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Session);

        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Session approval revoked.");
        updatedDocument.Should().NotBeNull().And.Contain("No grants.");
    }

    [Fact]
    public async Task Revoking_session_and_always_grants_needs_no_document_subscriber()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Always));
        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Session);
        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.ProposeRestart, ModelApprovalScope.Always);

        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Revoking_always_grant_notifies_grant_document_subscribers()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        string? updatedDocument = null;
        fixture.ViewModel.GrantDocumentChanged += (_, document) => updatedDocument = document;

        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Always);

        updatedDocument.Should().NotBeNull().And.Contain("No grants.");
    }

    [Fact]
    public async Task Confirming_and_clearing_a_session_grant_notifies_document_subscribers()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var documents = new List<string>();
        fixture.ViewModel.GrantDocumentChanged += (_, markdown) => documents.Add(markdown);
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));

        await fixture.ViewModel.ConfirmGrantChangeAsync();
        await fixture.ViewModel.ExitAsync();

        documents.Should().HaveCount(2);
        documents[0].Should().Contain("**LockMachine**");
        documents[1].Should().Contain("No grants.");
    }

    [Fact]
    public async Task Grant_document_lists_each_registered_persistent_grant_without_running_it()
    {
        var fixture = new Fixture();
        var actions = fixture.Catalog.GetCommands("Kora").Select(command => command.Action).Distinct().ToArray();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, actions));
        await fixture.ViewModel.InitializeAsync();

        var document = fixture.ViewModel.GetGrantDocument();

        foreach (var action in actions)
        {
            document.Should().Contain($"**{action}**");
        }
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Events.Should().NotContain("process.restart");
    }

    [Fact]
    public async Task Built_in_commands_take_precedence_over_a_ready_model()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("Kora, open preferences");

        fixture.ViewModel.ResponseTitle.Should().Be("Settings");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Model_execution_commands_persist_and_report_local_and_hosted_choices()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("which models are enabled");
        fixture.ViewModel.ResponseBody.Should().Contain(
            "Local models are enabled and Ollama is ready.");

        await fixture.RunAsync("disable local models");
        fixture.ViewModel.LocalModelsEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Local models disabled.");

        await fixture.RunAsync("enable hosted models");
        fixture.ViewModel.HostedModelsEnabled.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Contain("no hosted provider is configured");

        await fixture.RunAsync("which models are enabled");
        fixture.ViewModel.ResponseTitle.Should().Be("Model execution settings");
        fixture.ViewModel.ResponseBody.Should().Contain("Local models are disabled");
        fixture.ViewModel.ResponseBody.Should().Contain("Hosted models are enabled");
        fixture.ModelExecutionPreferences.Settings.Should().Be(new ModelExecutionSettings(
            LocalModelsEnabled: false,
            HostedModelsEnabled: true));
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(entry =>
            entry.ActionId == "configuration.model-execution"
            && entry.Initiator == SecurityAuditInitiator.TypedCommand
            && entry.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Model_execution_properties_ignore_duplicates_and_update_hosted_description()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        fixture.ViewModel.LocalModelsEnabled = true;
        fixture.ViewModel.HostedModelsEnabled = false;
        fixture.Audit.Events.Should().BeEmpty();
        fixture.ViewModel.HostedModelExecutionDescription.Should().StartWith(
            "Hosted model use is blocked.");

        fixture.ViewModel.HostedModelsEnabled = true;
        fixture.ViewModel.HostedModelExecutionDescription.Should().StartWith(
            "Hosted model use is allowed,");
        fixture.ViewModel.HostedModelsEnabled = false;

        fixture.ViewModel.HostedModelsEnabled.Should().BeFalse();
        fixture.ViewModel.HostedModelExecutionDescription.Should().StartWith(
            "Hosted model use is blocked.");
        fixture.Audit.Events.Should().HaveCount(4);
    }

    [Fact]
    public async Task Model_execution_commands_cover_each_availability_state()
    {
        var fixture = await Fixture.CreateInitializedAsync();

        await fixture.RunAsync("which models are enabled");
        fixture.ViewModel.ResponseBody.Should().Contain(
            "Local models are enabled, but Ollama is not ready.");
        fixture.ViewModel.ResponseBody.Should().Contain("Hosted models are disabled.");

        await fixture.RunAsync("enable local models");
        fixture.ViewModel.ResponseTitle.Should().Be("Local models enabled.");
        await fixture.RunAsync("disable hosted models");
        fixture.ViewModel.ResponseTitle.Should().Be("Hosted models disabled.");

        await fixture.RunAsync("enable hosted models");
        await fixture.RunAsync("explain this concept");
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");
        fixture.ViewModel.ResponseBody.Should().Contain(
            "Hosted models are allowed, but no hosted provider is configured");

        await fixture.RunAsync("disable local models");
        await fixture.RunAsync("explain this concept");
        fixture.ViewModel.ResponseTitle.Should().Be("No enabled model is available.");

        await fixture.RunAsync("what can you do");
        fixture.ViewModel.ResponseBody.Should().Contain("Local model use is disabled");
        await fixture.RunAsync("what version are you running");
        fixture.ViewModel.ResponseBody.Should().Contain("local models disabled");

        await fixture.RunAsync("enable local models");
        fixture.ViewModel.LocalModelsEnabled.Should().BeTrue();
        await fixture.RunAsync("disable hosted models");
        fixture.ViewModel.HostedModelsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("enable local models", false, false)]
    [InlineData("disable local models", true, false)]
    [InlineData("enable hosted models", true, false)]
    [InlineData("disable hosted models", true, true)]
    public async Task Failed_model_execution_commands_do_not_report_success(
        string command,
        bool localModelsEnabled,
        bool hostedModelsEnabled)
    {
        var fixture = new Fixture();
        fixture.ModelExecutionPreferences.Save(new ModelExecutionSettings(
            localModelsEnabled,
            hostedModelsEnabled));
        await fixture.ViewModel.InitializeAsync();
        fixture.ModelExecutionPreferences.SaveFailure = new IOException("disk unavailable");

        await fixture.RunAsync(command);

        fixture.ViewModel.ResponseTitle.Should().Be("Model settings could not be saved.");
        fixture.ViewModel.LocalModelsEnabled.Should().Be(localModelsEnabled);
        fixture.ViewModel.HostedModelsEnabled.Should().Be(hostedModelsEnabled);
    }

    [Fact]
    public async Task Disabled_local_models_block_free_form_requests_even_when_ollama_is_ready()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.ModelExecutionPreferences.Save(new ModelExecutionSettings(
            LocalModelsEnabled: false,
            HostedModelsEnabled: false));
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("explain this concept");

        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Model use is turned off.");
        fixture.ViewModel.ResponseBody.Should().Contain("Local and hosted models are disabled");
    }

    [Fact]
    public async Task Disabling_local_models_tolerates_an_already_cancelled_reasoning_request()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var field = typeof(MainViewModel).GetField(
            "activeReasoningCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(fixture.ViewModel, cancellation);

        try
        {
            fixture.ViewModel.LocalModelsEnabled = false;
        }
        finally
        {
            field.SetValue(fixture.ViewModel, null);
        }

        fixture.ViewModel.LocalModelsEnabled.Should().BeFalse();
        cancellation.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Disabling_local_models_cancels_an_in_flight_ollama_request()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Gate = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("explain this concept");
        var reasoning = fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.LocalModelsEnabled = false;
        await reasoning;

        fixture.ViewModel.LocalModelsEnabled.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Local request cancelled.");
        fixture.ViewModel.SetupTasks.Should().Contain(task =>
            task.Id == "local.reasoning" && task.State == SetupTaskState.Cancelled);
    }

    [Fact]
    public async Task Voice_can_change_model_execution_without_invoking_a_model()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();

        await fixture.RaiseActivatedTranscriptAsync("Kora, disable local models", 0.9f);

        fixture.ViewModel.LocalModelsEnabled.Should().BeFalse();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(entry =>
            entry.ActionId == "configuration.model-execution"
            && entry.Initiator == SecurityAuditInitiator.VoiceCommand
            && entry.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Failed_model_execution_save_keeps_the_effective_setting()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ModelExecutionPreferences.SaveFailure = new IOException("disk unavailable");

        fixture.ViewModel.LocalModelsEnabled = false;

        fixture.ViewModel.LocalModelsEnabled.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Model settings could not be saved.");
        fixture.Audit.Events.Should().Contain(entry =>
            entry.ActionId == "configuration.model-execution"
            && entry.Outcome == SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task Model_can_request_a_read_only_built_in_action_through_the_host()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ShowVersion;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("tell me your running version");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.Reasoner.LastContext!.Dependencies.Should().Contain(status =>
            status.Name == "Local model inference (Ollama)"
            && status.Readiness == DependencyReadiness.Ready);
        fixture.Reasoner.LastContext.Tasks.Should().NotContain(task =>
            task.Name == "Local model response");
        fixture.ViewModel.ResponseTitle.Should().Be("Kora version");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Model_requested_session_lock_waits_for_explicit_approval()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        fixture.ViewModel.IsVisualResponseVisible.Should().BeFalse();

        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Contain("Lock the current Windows session.");
        await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync();

        fixture.Session.LockCalls.Should().Be(1);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Audit.Events.Should().Contain(entry =>
            entry.ActionId == "model.action.lockmachine"
            && entry.Initiator == SecurityAuditInitiator.ModelSuggestion
            && entry.Outcome == SecurityAuditOutcome.Succeeded);

        await fixture.RunAsync("please lock this workstation again");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(BuiltInAction.HideApplication)]
    [InlineData(BuiltInAction.ExitApplication)]
    [InlineData(BuiltInAction.RestartApplication)]
    [InlineData(BuiltInAction.CancelTask)]
    [InlineData(BuiltInAction.ProposeShutdown)]
    [InlineData(BuiltInAction.ProposeRestart)]
    public async Task Other_disruptive_model_actions_wait_for_user_approval(
        BuiltInAction action)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = action;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("please do something with the application");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Approve a model-suggested action?");
        fixture.WindowActions.Should().NotContain(WindowAction.Close);
        fixture.Events.Should().NotContain("process.restart");
        fixture.ViewModel.RejectPendingModelAction();
    }

    [Fact]
    public async Task Approved_model_exit_finishes_after_reasoning_without_waiting_on_itself()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ExitApplication;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please close the application");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.WindowActions.Should().Contain(WindowAction.Close);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Session_approval_reuses_the_exact_action_without_a_second_prompt()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ProposeShutdown;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("prepare shutting this computer down");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.ApproveModelActionForSessionCommand.ExecuteAsync();
        fixture.ViewModel.SessionAllowedModelActions.Should().Contain(command =>
            command.Action == BuiltInAction.ProposeShutdown);
        await fixture.RunAsync("prepare shutting this computer down again");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Shutdown request recognized.");
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();

        fixture.Reasoner.Action = BuiltInAction.ProposeRestart;
        await fixture.RunAsync("prepare a computer reboot sometime");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
    }

    [Fact]
    public async Task Locking_windows_through_kora_clears_session_grants()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ProposeShutdown;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("prepare the shutdown");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.ViewModel.ApproveModelActionForSessionCommand.ExecuteAsync();
        fixture.ViewModel.SessionAllowedModelActions.Should().ContainSingle();

        await fixture.RunAsync("lock the machine");
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        await fixture.RunAsync("prepare a different shutdown");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
    }

    [Fact]
    public async Task Always_approval_for_lock_is_persisted_and_survives_the_lock()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock the workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should()
            .Contain(BuiltInAction.LockMachine);
        fixture.Session.LockCalls.Should().Be(1);

        await fixture.RunAsync("please lock the workstation again");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Session.LockCalls.Should().Be(2);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.ViewModel.RevokeModelActionApproval(
            BuiltInAction.LockMachine, ModelApprovalScope.Always);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_always_grant_is_reloaded_before_model_actions_are_handled()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(
            true, [BuiltInAction.LockMachine]));
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("lock the workstation please");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Session.LockCalls.Should().Be(1);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Granted_model_exit_does_not_wait_on_its_own_reasoning_task()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(
            true, [BuiltInAction.ExitApplication]));
        fixture.Reasoner.Action = BuiltInAction.ExitApplication;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("close the app please");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.WindowActions.Should().Contain(WindowAction.Close);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Granted_model_restart_dispatches_without_waiting_on_its_own_reasoning_task()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(
            new ModelApprovalPreferences(true, [BuiltInAction.RestartApplication]));
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.RestartApplication;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("would you relaunch this software now");
        await fixture.ViewModel.ActiveReasoningTask!.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Events.Should().Contain("process.restart");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task List_and_manage_grants_work_without_a_model()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        string? document = null;
        fixture.ViewModel.GrantDocumentRequested += (_, markdown) => document = markdown;

        await fixture.RunAsync("list grants");
        document.Should().Contain("## This session").And.Contain("## Always on this device");
        document.Should().Contain("No grants.");

        await fixture.RunAsync("manage grants");
        fixture.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        fixture.ViewModel.SelectedGrantAction = fixture.ViewModel.GrantActions.Single(item =>
            item.Action == BuiltInAction.LockMachine);
        fixture.ViewModel.SelectedGrantScope = ModelApprovalScope.Always;
        await fixture.ViewModel.PrepareGrantChangeCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("Add Always grant for LockMachine.");
        fixture.Session.LockCalls.Should().Be(0);

        await fixture.ViewModel.ConfirmGrantChangeCommand.ExecuteAsync();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should()
            .Contain(BuiltInAction.LockMachine);
        fixture.Session.LockCalls.Should().Be(0);

        await fixture.RunAsync("view grants");
        document.Should().Contain("**LockMachine**");
    }

    [Fact]
    public async Task Session_grant_can_be_added_removed_and_inferred_without_executing_action()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var action = BuiltInAction.LockMachine;
        fixture.ViewModel.PrepareGrantChange(new GrantChange(GrantChangeOperation.Add, action, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.SessionAllowedModelActions.Should().ContainSingle(command => command.Action == action);
        fixture.ViewModel.GetGrantDocument().Should().Contain("**LockMachine**");

        fixture.ViewModel.PrepareGrantChange(new GrantChange(GrantChangeOperation.Remove, action, ModelApprovalScope.Once));
        fixture.ViewModel.ResponseBody.Should().Contain("Remove Session grant");
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Moving_session_grant_to_always_persists_only_the_target_scope()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var action = BuiltInAction.LockMachine;
        fixture.ViewModel.PrepareGrantChange(new GrantChange(GrantChangeOperation.Add, action, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Move, action, ModelApprovalScope.Session, ModelApprovalScope.Always));
        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().ContainSingle(command => command.Action == action);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().ContainSingle().Which.Should().Be(action);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(GrantChangeOperation.Add, ModelApprovalScope.Once, null, "Choose a grant scope.")]
    [InlineData(GrantChangeOperation.Remove, ModelApprovalScope.Once, null, "Select the exact existing grant.")]
    [InlineData(GrantChangeOperation.Remove, ModelApprovalScope.Session, null, "Grant change cannot be prepared.")]
    [InlineData(GrantChangeOperation.Move, ModelApprovalScope.Session, ModelApprovalScope.Always, "Grant change cannot be prepared.")]
    [InlineData(GrantChangeOperation.Add, ModelApprovalScope.Session, ModelApprovalScope.Always, "Grant change cannot be prepared.")]
    public void Invalid_or_inapplicable_grant_changes_are_not_pending(
        GrantChangeOperation operation, ModelApprovalScope scope, ModelApprovalScope? target, string title)
    {
        var fixture = new Fixture();

        fixture.ViewModel.PrepareGrantChange(new GrantChange(operation, BuiltInAction.LockMachine, scope, target));

        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Grant_confirmation_without_pending_change_fails_and_rejection_is_safe()
    {
        var fixture = new Fixture();

        var action = () => fixture.ViewModel.ConfirmGrantChangeAsync();
        await action.Should().ThrowAsync<InvalidOperationException>();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_grants_keeps_failed_persistent_writes_and_ignores_absent_grants()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Session);
        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.ShowVersion, ModelApprovalScope.Always);
        fixture.ApprovalPreferences.SaveFailure = new IOException("disk unavailable");

        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Always);

        fixture.ViewModel.AlwaysAllowedModelActions.Should()
            .ContainSingle(command => command.Action == BuiltInAction.LockMachine);
        fixture.ViewModel.ResponseTitle.Should().Be("Model approval settings could not be saved.");
    }

    [Fact]
    public void Invalid_grant_proposals_and_revocations_are_rejected()
    {
        var fixture = new Fixture();
        var nullProposal = () => fixture.ViewModel.PrepareGrantChange(null!);
        var invalidAction = () => fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, (BuiltInAction)9999, ModelApprovalScope.Session));
        var invalidOperation = () => fixture.ViewModel.PrepareGrantChange(
            new GrantChange((GrantChangeOperation)9999, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        var invalidScope = () => fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, BuiltInAction.LockMachine, (ModelApprovalScope)9999));
        var invalidTarget = () => fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Move, BuiltInAction.LockMachine,
                ModelApprovalScope.Session, ModelApprovalScope.Once));
        var invalidRevoke = () => fixture.ViewModel.RevokeModelActionApproval(
            (BuiltInAction)9999, ModelApprovalScope.Session);
        var invalidRevokeScope = () => fixture.ViewModel.RevokeModelActionApproval(
            BuiltInAction.LockMachine, (ModelApprovalScope)9999);

        nullProposal.Should().Throw<ArgumentNullException>();
        invalidAction.Should().Throw<ArgumentException>();
        invalidOperation.Should().Throw<ArgumentException>();
        invalidScope.Should().Throw<ArgumentException>();
        invalidTarget.Should().Throw<ArgumentException>();
        invalidRevoke.Should().Throw<ArgumentOutOfRangeException>();
        invalidRevokeScope.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Failed_voice_approval_preference_write_preserves_the_existing_setting()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = true;
        fixture.ApprovalPreferences.SaveFailure = new IOException("preferences unavailable");

        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;

        fixture.ViewModel.RequireAssistantNameForVoiceApproval.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.RequireAssistantNameForVoiceApproval.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Model approval settings could not be saved.");
    }

    [Fact]
    public async Task Grant_editor_selection_can_be_changed_without_preparing_an_action()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.RunAsync("manage grants");
        var action = fixture.ViewModel.GrantActions.Single(command => command.Action == BuiltInAction.LockMachine);

        fixture.ViewModel.SelectedGrantAction = action;
        fixture.ViewModel.SelectedGrantAction = action;
        fixture.ViewModel.SelectedGrantOperation = GrantChangeOperation.Remove;
        fixture.ViewModel.SelectedGrantOperation = GrantChangeOperation.Remove;
        fixture.ViewModel.SelectedGrantScope = ModelApprovalScope.Always;
        fixture.ViewModel.SelectedGrantScope = ModelApprovalScope.Always;
        fixture.ViewModel.SelectedGrantTargetScope = ModelApprovalScope.Session;
        fixture.ViewModel.SelectedGrantTargetScope = ModelApprovalScope.Session;

        fixture.ViewModel.SelectedGrantTargetScope.Should().Be(ModelApprovalScope.Session);
        fixture.ViewModel.IsGrantEditorVisible.Should().BeTrue();
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Removing_an_inferred_single_grant_requires_confirmation_and_never_executes_it()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();

        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Once));

        fixture.ViewModel.ResponseBody.Should().Contain("Remove Always grant for LockMachine.");
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().Contain(BuiltInAction.LockMachine);
        await fixture.RunAsync("approve once");
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Ambiguous_removal_requires_scope_and_a_superseded_change_cannot_apply()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Once));
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Select the exact existing grant.");

        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await fixture.RunAsync("show task progress");
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().Contain(BuiltInAction.LockMachine);
    }

    [Fact]
    public async Task Persistent_grant_write_failure_retains_the_pending_change_and_existing_grants()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ApprovalPreferences.SaveFailure = new IOException("disk unavailable");
        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));

        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Model approval settings could not be saved.");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Grant_confirmation_and_rejection_keep_the_change_pending_if_speech_cannot_stop()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.GrantChange =
            new GrantChange(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("allow local lock suggestions");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopException = new IOException("audio device unavailable");

        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Could not stop the spoken grant question.");
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();

        fixture.TextToSpeech.StopException = null;
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Superseding_a_grant_during_spoken_confirmation_prevents_the_old_change()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.GrantChange = new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("allow suggestions to lock Windows");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var confirming = fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));
        fixture.TextToSpeech.StopGate.SetResult();
        await confirming.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
    }

    [Fact]
    public async Task A_new_grant_proposal_supersedes_a_pending_model_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
    }

    [Fact]
    public async Task Grant_and_model_action_prompts_work_without_window_or_document_subscribers()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        await fixture.RunAsync("list grants");
        await fixture.RunAsync("manage grants");
        fixture.ViewModel.RejectPendingModelAction();
        fixture.ViewModel.RejectPendingModelAction();
        fixture.WindowActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Moving_a_grant_changes_scope_atomically_without_running_the_action()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Move, BuiltInAction.LockMachine,
            ModelApprovalScope.Always, ModelApprovalScope.Session));

        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.SessionAllowedModelActions.Should()
            .ContainSingle(command => command.Action == BuiltInAction.LockMachine);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Existing_grant_cannot_be_added_or_moved_to_an_occupied_target()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var action = BuiltInAction.LockMachine;
        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, action, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.ViewModel.PrepareGrantChange(
            new GrantChange(GrantChangeOperation.Add, action, ModelApprovalScope.Always));
        await fixture.ViewModel.ConfirmGrantChangeAsync();

        var attempts = new[]
        {
            new GrantChange(GrantChangeOperation.Add, action, ModelApprovalScope.Session),
            new GrantChange(GrantChangeOperation.Remove, action, ModelApprovalScope.Session, ModelApprovalScope.Always),
            new GrantChange(GrantChangeOperation.Move, action, ModelApprovalScope.Session, ModelApprovalScope.Session),
            new GrantChange(GrantChangeOperation.Move, action, ModelApprovalScope.Session, ModelApprovalScope.Always),
        };
        foreach (var change in attempts)
        {
            fixture.ViewModel.PrepareGrantChange(change);
            fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
            fixture.ViewModel.ResponseTitle.Should().Be("Grant change cannot be prepared.");
        }
        fixture.ViewModel.SessionAllowedModelActions.Should().ContainSingle();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().ContainSingle();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_grant_revoked_before_confirmation_is_not_changed_again()
    {
        var fixture = new Fixture();
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        fixture.ViewModel.RevokeModelActionApproval(BuiltInAction.LockMachine, ModelApprovalScope.Always);

        await fixture.ViewModel.ConfirmGrantChangeAsync();

        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("The grant changed before confirmation.");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Model_grant_proposal_requires_visible_spoken_or_mouse_confirmation()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.ApprovalPreferences.Save(new ModelApprovalPreferences(true, [BuiltInAction.LockMachine]));
        fixture.Reasoner.GrantChange = new GrantChange(
            GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Always);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("remove my always grant for locking Windows");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Contain("Remove Always grant for LockMachine.");
        await fixture.RunAsync("always allow this");
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().Contain(BuiltInAction.LockMachine);

        await fixture.RunAsync("approve once");
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Voice_grant_confirmation_requires_the_assistant_name_by_default()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));

        await fixture.RaiseActivatedTranscriptAsync("approve once", 0.9f);
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        await fixture.RaiseActivatedTranscriptAsync("Kora, approve once", 0.9f);

        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should()
            .Contain(BuiltInAction.LockMachine);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Voice_grant_rejection_and_non_once_replies_never_change_permission()
    {
        var fixture = new Fixture();
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        var change = new GrantChange(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
        fixture.ViewModel.PrepareGrantChange(change);

        await fixture.RaiseActivatedTranscriptAsync("Kora, always allow this", 0.9f);
        fixture.ViewModel.ResponseTitle.Should().Be("Confirm the exact change once.");
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();

        await fixture.RaiseActivatedTranscriptAsync("Kora, reject", 0.9f);
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Model_question_is_visible_and_mouse_choice_continues_without_approving_an_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which action?", ["Lock it", "Explain"]);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;

        await fixture.RunAsync("help me with this computer");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.ModelQuestionChoices.Select(choice => choice.DisplayText)
            .Should().Equal("1. Lock it", "2. Explain");
        var selected = fixture.ViewModel.ModelQuestionChoices[0];
        fixture.Reasoner.Question = null;
        fixture.Reasoner.Action = BuiltInAction.LockMachine;

        await fixture.ViewModel.SelectModelQuestionChoiceAsync(selected);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().HaveCount(2);
        fixture.Reasoner.Requests[1].Should().Contain("help me with this computer")
            .And.Contain("Lock it").And.Contain("not permission");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
    }

    [Fact]
    public async Task Question_speech_requires_name_and_stale_choice_is_ignored()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which color?", ["Blue", "Green"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("pick a color");
        await fixture.ViewModel.ActiveReasoningTask!;
        var oldChoice = fixture.ViewModel.ModelQuestionChoices[0];
        await fixture.RaiseActivatedTranscriptAsync("option one", 0.9f);
        fixture.Voice.StartedPhrases.Should().Contain("Kora option one");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.Reasoner.Requests.Should().ContainSingle();
        await fixture.ViewModel.CancelModelQuestionAsync();
        await fixture.ViewModel.SelectModelQuestionChoiceAsync(oldChoice);
        fixture.Reasoner.Requests.Should().ContainSingle();

        await fixture.RunAsync("pick another color");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Question = null;
        await fixture.RaiseActivatedTranscriptAsync("Kora, option two", 0.9f);
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Requests.Should().HaveCount(3);
        fixture.Reasoner.Requests[2].Should().Contain("Green");
    }

    [Fact]
    public async Task Unknown_question_answer_remains_pending_until_a_built_in_command_supersedes_it()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which color?", ["Blue", "Green"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose a color");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RaiseActivatedTranscriptAsync("maybe later", 0.9f);
        fixture.ViewModel.ResponseTitle.Should().Be("Choose an option or cancel the question.");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await fixture.RaiseActivatedTranscriptAsync("Kora, what version are you running", 0.9f);
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Kora version");
        fixture.Reasoner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Spoken_cancel_question_dismisses_it_without_a_followup()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which color?", ["Blue", "Green"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose a color");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RaiseActivatedTranscriptAsync("Kora, cancel question", 0.9f);

        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.ViewModel.ResponseTitle.Should().Be("Question dismissed.");
    }

    [Fact]
    public async Task Question_can_be_answered_unprefixed_when_configured_without_speech_output()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Continue?", ["Yes", "No"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly;
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        await fixture.RunAsync("should I proceed");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.Reasoner.Question = null;

        await fixture.RaiseActivatedTranscriptAsync("yes", 0.9f);
        fixture.Voice.StartedPhrases.Should().Contain("yes");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().HaveCount(2);
        fixture.Reasoner.Requests[1].Should().Contain("\"SelectedOption\":\"Yes\"");
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
    }

    [Fact]
    public async Task Question_rejection_does_not_send_a_followup_request()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which one?", ["A", "B"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RunAsync("reject");

        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Question dismissed.");
        fixture.Reasoner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Question_choice_rejects_null_and_unrelated_choices_without_followup()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        var invalid = () => fixture.ViewModel.SelectModelQuestionChoiceAsync(null!);
        await invalid.Should().ThrowAsync<ArgumentNullException>();
        await fixture.ViewModel.CancelModelQuestionAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.SelectModelQuestionChoiceAsync(new ModelQuestionChoice(1, "One"));

        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.Reasoner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Model_question_is_available_without_a_window_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which option?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ModelQuestionChoices.Should().HaveCount(2);
        await fixture.ViewModel.CancelModelQuestionAsync();
    }

    [Fact]
    public async Task Failed_question_prompt_stop_preserves_pending_choice_and_cancellation()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("choose");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopException = new IOException("audio device unavailable");

        await fixture.ViewModel.SelectModelQuestionChoiceAsync(fixture.ViewModel.ModelQuestionChoices[0]);
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Could not stop the spoken question.");
        await fixture.ViewModel.CancelModelQuestionAsync();
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.Reasoner.Requests.Should().ContainSingle();

        fixture.TextToSpeech.StopException = null;
        await fixture.ViewModel.CancelModelQuestionAsync();
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        await fixture.ViewModel.ActiveReasoningTask!;
    }

    [Fact]
    public async Task Failed_approval_audio_stop_does_not_dismiss_the_pending_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("please lock this workstation");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopException = new IOException("playback cannot stop");

        var dismissed = await fixture.ViewModel.RejectPendingModelActionAsync();

        dismissed.Should().BeFalse();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Approval prompt could not be stopped.");
        fixture.ViewModel.ResponseBody.Should().Contain("playback cannot stop");

        fixture.TextToSpeech.StopException = null;
        (await fixture.ViewModel.RejectPendingModelActionAsync()).Should().BeTrue();
        await fixture.ViewModel.ActiveReasoningTask!;
    }

    [Fact]
    public async Task Rejecting_without_a_pending_model_action_is_a_successful_no_op()
    {
        var fixture = new Fixture();

        var rejected = await fixture.ViewModel.RejectPendingModelActionAsync();

        rejected.Should().BeTrue();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.TextToSpeech.StopCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dismissing_a_question_while_prompt_stop_is_in_flight_does_not_apply_a_stale_choice(
        bool cancel)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("choose");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = fixture.ViewModel.ModelQuestionChoices[0];

        var stopping = cancel
            ? fixture.ViewModel.CancelModelQuestionAsync()
            : fixture.ViewModel.SelectModelQuestionChoiceAsync(choice);
        await fixture.TextToSpeech.StopStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.HideApplication();
        fixture.TextToSpeech.StopGate.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Oversized_clarification_followup_does_not_go_back_to_the_model()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(new string('a', 4080));
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.SelectModelQuestionChoiceAsync(fixture.ViewModel.ModelQuestionChoices[0]);

        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.ViewModel.ResponseTitle.Should().Be("The clarification could not continue.");
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_model_questions_stop_after_three_clarifications()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which choice?", ["A", "B"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose for me");
        await fixture.ViewModel.ActiveReasoningTask!;

        for (var index = 0; index < 3; index++)
        {
            await fixture.ViewModel.SelectModelQuestionChoiceAsync(
                fixture.ViewModel.ModelQuestionChoices[0]);
            await fixture.ViewModel.ActiveReasoningTask!;
        }

        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Too many clarification questions.");
        fixture.Reasoner.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task Spoken_approval_requires_the_name_and_new_PTT_after_the_question()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
        await fixture.RaiseActivatedTranscriptAsync("always allow this", 0.9f);
        fixture.Voice.StartedPhrases.Should().Contain("Kora always allow this");
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();

        await fixture.RaiseActivatedTranscriptAsync("Kora, always allow this", 0.9f);

        fixture.Session.LockCalls.Should().Be(1);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should()
            .Contain(BuiltInAction.LockMachine);
    }

    [Fact]
    public async Task Spoken_approval_can_allow_unprefixed_replies_when_configured()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ProposeRestart;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        await fixture.RunAsync("please propose a reboot");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RaiseActivatedTranscriptAsync("yes for this session", 0.9f);

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.ViewModel.SessionAllowedModelActions.Should().Contain(command =>
            command.Action == BuiltInAction.ProposeRestart);
        fixture.ApprovalPreferences.Preferences.RequireAssistantNameForVoiceApproval.Should().BeFalse();
    }

    [Fact]
    public async Task Approving_while_the_question_is_spoken_stops_it_before_the_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ProposeRestart;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("please propose a restart");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().NotContain("Kora, approve");
        await fixture.ViewModel.ApproveModelActionCommand.ExecuteAsync()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
        fixture.ViewModel.ResponseTitle.Should().Be("Restart request recognized.");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_persistent_approval_write_does_not_execute_the_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        fixture.ApprovalPreferences.SaveFailure = new IOException("disk unavailable");
        await fixture.RunAsync("lock this session please");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync();

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("Model approval settings could not be saved.");
    }

    [Fact]
    public async Task Superseding_a_pending_request_while_approval_audio_stops_prevents_the_old_grant()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("lock this workstation please");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var approving = fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync();
        approving.IsCompleted.Should().BeFalse();
        var superseding = fixture.RunAsync("show task progress");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.TextToSpeech.StopGate.SetResult();
        await superseding.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await approving.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Session.LockCalls.Should().Be(0);
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Locking_while_a_model_routed_response_is_speaking_stops_playback_first()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.ShowPowerStatus;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("tell me which release this is");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await fixture.RunAsync("lock the machine")
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Session.LockCalls.Should().Be(1);
        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Rejecting_or_superseding_a_model_action_never_runs_it()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock the workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.ViewModel.RejectModelActionCommand.ExecuteAsync();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Audit.Events.Should().Contain(entry =>
            entry.ActionId == "model.action.lockmachine"
            && entry.Outcome == SecurityAuditOutcome.Cancelled);

        await fixture.RunAsync("lock the workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        await fixture.RunAsync("show task progress");

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Spoken_command_variant_uses_built_in_route_before_a_ready_model()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();

        await fixture.RaiseActivatedTranscriptAsync("Kora, show the task queue", 0.9f);
        fixture.Voice.StartedPhrases.Should().Contain("Kora show the task queue");

        fixture.ViewModel.ResponseTitle.Should().Be("Waiting for your command.");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Freeform_voice_requires_the_active_name_before_local_inference()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();

        await fixture.RaiseActivatedTranscriptAsync("why is the sky blue", 0.9f);
        fixture.Reasoner.Requests.Should().BeEmpty();
        await fixture.RaiseActivatedTranscriptAsync("Kora, why is the sky blue", 0.9f);
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.Reasoner.Requests.Should().ContainSingle().Which.Should().Be("why is the sky blue");
        fixture.ViewModel.ResponseTitle.Should().Be("Local model response");
    }

    [Fact]
    public async Task Cancel_task_interrupts_local_reasoning_without_reporting_an_answer()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.Reasoner.Gate = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("explain this concept");
        var running = fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsLocalTaskCancellable.Should().BeTrue();
        fixture.ViewModel.IsCancelTaskVisible.Should().BeTrue();

        await fixture.ViewModel.CancelCurrentTaskAsync();
        await running;

        fixture.ViewModel.IsLocalTaskCancellable.Should().BeFalse();
        fixture.ViewModel.SetupTasks.Should().Contain(task =>
            task.Id == "local.reasoning" && task.State == SetupTaskState.Cancelled);
        fixture.ViewModel.ResponseTitle.Should().NotBe("Local model response");
        fixture.Reasoner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Built_in_status_stays_available_and_a_second_model_request_is_not_started()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.Reasoner.Gate = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("first question");
        var running = fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RunAsync("show task progress");
        fixture.ViewModel.ResponseTitle.Should().Be("Local model response: running.");
        fixture.ViewModel.ResponseBody.Should().Contain("Generating an answer locally.");
        await fixture.RunAsync("second question");
        fixture.ViewModel.ResponseTitle.Should().Be("Local reasoning is busy.");
        fixture.Reasoner.Requests.Should().ContainSingle().Which.Should().Be("first question");

        fixture.Reasoner.Gate.SetResult("Finished.");
        await running;
        fixture.ViewModel.ResponseBody.Should().Contain("Finished.");
    }

    [Fact]
    public async Task Refresh_during_reasoning_reports_the_active_task_instead_of_probing()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();
        fixture.Reasoner.Gate = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("explain this concept");
        var reasoning = fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.DetectMicrophonesAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Local reasoning is running.");
        fixture.Reasoner.Gate.SetResult("Explanation.");
        await reasoning;
    }

    [Fact]
    public async Task Locking_while_model_speech_is_playing_stops_playback_before_waiting_for_the_task()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("explain this");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await fixture.RunAsync("lock the machine")
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.Session.LockCalls.Should().Be(1);
        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Cancelling_while_model_speech_is_playing_stops_playback()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("explain this");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ViewModel.IsLocalTaskCancellable.Should().BeTrue();

        await fixture.ViewModel.CancelCurrentTaskAsync()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        fixture.TextToSpeech.StopCalls.Should().BeGreaterThan(0);
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
        fixture.ViewModel.IsLocalTaskCancellable.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("Local answer already completed.");
    }

    [Fact]
    public async Task Cancelling_model_speech_contains_stop_failures_and_retains_active_state()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("explain this");
        var reasoning = fixture.ViewModel.ActiveReasoningTask!;
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopException = new IOException("playback cannot stop");

        Func<Task> action = () => fixture.ViewModel.CancelCurrentTaskAsync();

        await action.Should().NotThrowAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Speech output could not be stopped.");
        fixture.ViewModel.ResponseBody.Should().Contain("playback cannot stop");
        fixture.ViewModel.IsSpeaking.Should().BeTrue();
        fixture.ViewModel.IsCancelTaskVisible.Should().BeTrue();

        fixture.TextToSpeech.SpeakGate.SetResult();
        await reasoning;
    }

    [Fact]
    public async Task Failed_local_inference_disables_future_model_requests_until_refresh()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        await fixture.ViewModel.InitializeAsync();
        fixture.Reasoner.Failure = new HttpRequestException("Local runtime disconnected.");
        await fixture.RunAsync("ask something");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.Dependencies.Should().ContainSingle()
            .Which.Readiness.Should().Be(DependencyReadiness.Failed);
        fixture.ViewModel.LocalModelSetupStatus.Should()
            .Contain("Local inference failed: Local runtime disconnected.");
        fixture.ViewModel.ShouldOfferLocalModelSetup.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be("Local reasoning failed.");
        await fixture.RunAsync("another question");
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.ViewModel.ResponseTitle.Should().Be("That isn't a supported built-in command.");
    }

    [Theory]
    [InlineData("question", "Which option?")]
    [InlineData("grant", "Add Always grant for LockMachine.")]
    [InlineData("action", "Lock the current Windows session.")]
    public async Task Spoken_prompt_failure_keeps_the_exact_interaction_pending(
        string kind, string description)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        switch (kind)
        {
            case "question":
                fixture.Reasoner.Question = new LocalModelQuestion("Which option?", ["One", "Two"]);
                break;
            case "grant":
                fixture.Reasoner.GrantChange = new GrantChange(
                    GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always);
                break;
            case "action":
                fixture.Reasoner.Action = BuiltInAction.LockMachine;
                break;
        }
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StopException = new IOException("recognition cannot stop");

        await fixture.RunAsync("please decide");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.ResponseTitle.Should().Be("The spoken approval prompt could not complete.");
        fixture.ViewModel.ResponseBody.Should().Contain(description);
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Dismissed_model_action_is_not_reported_as_pending_when_speech_prompt_fails()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.BeforeStopFailure = fixture.ViewModel.HideApplication;
        fixture.Voice.StopException = new IOException("recognition cannot stop");

        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Be("The spoken approval prompt could not complete.");
        fixture.ViewModel.ResponseBody.Should().Contain("No model action remains pending.");
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Model_suggested_read_only_action_reports_execution_failure()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.ShowVersion;
        fixture.ApplicationInfo.Failure = new IOException("version information unavailable");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("tell me the installed version");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.ResponseTitle.Should().Be("The model-suggested action failed.");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
    }

    [Fact]
    public async Task Voice_rejection_of_model_action_does_not_execute_it()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RaiseActivatedTranscriptAsync("Kora, reject", 0.9f);

        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Voice_once_approval_does_not_create_a_persistent_or_session_grant()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.RaiseActivatedTranscriptAsync("Kora, approve once", 0.9f);

        fixture.Session.LockCalls.Should().Be(1);
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_approval_notifies_grant_document_subscribers()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.ProposeRestart;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("please propose a reboot");
        await fixture.ViewModel.ActiveReasoningTask!;
        string? document = null;
        fixture.ViewModel.GrantDocumentChanged += (_, markdown) => document = markdown;

        await fixture.ViewModel.ApproveModelActionForSessionCommand.ExecuteAsync();

        document.Should().Contain("**ProposeRestart**");
        fixture.ViewModel.SessionAllowedModelActions.Should().ContainSingle();
    }

    [Fact]
    public async Task Spoken_question_and_grant_can_omit_name_when_user_opted_out()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        fixture.Reasoner.Question = new LocalModelQuestion("Which option?", ["One", "Two"]);
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.TextToSpeech.SpokenText.Should().Contain("You may answer without addressing me by name.");
        await fixture.ViewModel.CancelModelQuestionAsync();

        fixture.Reasoner.Question = null;
        fixture.Reasoner.GrantChange = new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Session);
        await fixture.RunAsync("grant permission");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.TextToSpeech.SpokenText.Should().Contain("You may answer without addressing me by name.");
        await fixture.ViewModel.RejectPendingGrantChangeAsync();
    }

    [Theory]
    [InlineData("Kora")]
    [InlineData("Kora, ")]
    [InlineData("Kora:")]
    public async Task Assistant_name_without_a_question_does_not_reach_the_reasoner(string request)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync(request);

        fixture.ViewModel.ResponseTitle.Should().Be("No question was heard.");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_local_request_does_not_reach_the_reasoner()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync(new string('a', 4097));

        fixture.ViewModel.ResponseTitle.Should().Be("The request is too long.");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Readiness_and_help_can_be_requested_with_a_verified_local_model()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();
        var requests = 0;
        fixture.ViewModel.ReadinessRequested += (_, _) => requests++;

        fixture.ViewModel.ShowReadiness();
        await fixture.RunAsync("Kora, what can you do");

        requests.Should().Be(1);
        fixture.ViewModel.ResponseBody.Should().Contain("verified local model");
    }

    [Fact]
    public void Readiness_request_without_subscribers_does_not_change_application_state()
    {
        var fixture = new Fixture();

        fixture.ViewModel.ShowReadiness();

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
    }

    [Fact]
    public async Task Failed_approval_audio_stop_does_not_run_or_approve_model_action()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        fixture.Voice.Microphones = [new MicrophoneDevice("mic", "Mic")];
        fixture.Voice.DefaultMicrophoneId = "mic";
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.RunAsync("please lock this workstation");
        await fixture.TextToSpeech.SpeakStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.TextToSpeech.StopException = new IOException("playback cannot stop");

        await fixture.ViewModel.ApproveModelActionAlwaysCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("The approved action failed.");
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);

        fixture.TextToSpeech.StopException = null;
        await fixture.ViewModel.RejectModelActionCommand.ExecuteAsync();
        await fixture.ViewModel.ActiveReasoningTask!;
    }

    [Theory]
    [MemberData(nameof(InvalidLocalModelResponses))]
    public async Task Invalid_model_responses_fail_closed_without_executing_actions(
        LocalModelResponse response)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Response = response;
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("please decide");
        await fixture.ViewModel.ActiveReasoningTask!;

        fixture.ViewModel.ResponseTitle.Should().Be("Local reasoning failed.");
        fixture.ViewModel.Dependencies.Should().ContainSingle()
            .Which.Readiness.Should().Be(DependencyReadiness.Failed);
        fixture.ViewModel.IsResponseInteractionPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    public static TheoryData<LocalModelResponse> InvalidLocalModelResponses => new()
    {
        new LocalModelResponse(null, null),
        new LocalModelResponse("answer", BuiltInAction.LockMachine),
        new LocalModelResponse(" ", null),
        new LocalModelResponse(null, (BuiltInAction)9999),
        new LocalModelResponse(null, null,
            new GrantChange(GrantChangeOperation.Add, (BuiltInAction)9999, ModelApprovalScope.Session)),
        new LocalModelResponse(null, null,
            new GrantChange((GrantChangeOperation)9999, BuiltInAction.LockMachine, ModelApprovalScope.Session)),
        new LocalModelResponse(null, null, null, new LocalModelQuestion(" ", ["One", "Two"])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion(new string('x', 501), ["One", "Two"])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", null!)),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", ["Only one"])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", ["One", "Two", "Three", "Four", "Five"])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", ["One", " "])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", ["One", new string('x', 81)])),
        new LocalModelResponse(null, null, null, new LocalModelQuestion("Which?", ["Same", "Same"])),
    };

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
    public async Task Status_command_reports_setup_blockers_without_a_model()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference",
            "Local model inference (Ollama)",
            DependencyReadiness.Missing,
            "An approved Ollama installation is required.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("Kora, what do you have left to do");

        fixture.ViewModel.ResponseTitle.Should().Be("Setup needs attention.");
        fixture.ViewModel.ResponseBody.Should().Contain("Ollama");
        fixture.ViewModel.ResponseBody.Should().Contain("approved Ollama installation");

        fixture.Probe.Status = fixture.Probe.Status with { Readiness = DependencyReadiness.Ready };
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
        await fixture.RunAsync("Kora, what do you have left to do");
        fixture.ViewModel.ResponseTitle.Should().Be("Voice is not active.");
    }

    [Fact]
    public async Task Status_command_remains_available_while_a_setup_task_is_running()
    {
        var fixture = new Fixture();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync();
        fixture.ViewModel.SetupTasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Running);

        fixture.ViewModel.CommandText = "what are you currently working on";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Setting up Storage.");

        fixture.Probe.Gate.SetResult();
        await refresh;
        fixture.ViewModel.SetupTasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Completed);
    }

    [Fact]
    public async Task Current_task_progress_command_reports_the_running_stage_during_a_busy_probe()
    {
        var fixture = new Fixture();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = fixture.ViewModel.RefreshCommand.ExecuteAsync();

        fixture.ViewModel.CommandText = "Kora, what is the current task progress";
        fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Storage: running.");
        fixture.ViewModel.ResponseBody.Should().Contain("Checking Storage.");
        fixture.ViewModel.ResponseBody.Should().Contain("No completion percentage");
        fixture.Probe.Gate.SetResult();
        await refresh;
    }

    [Fact]
    public async Task Current_task_progress_command_reports_blockers_when_nothing_is_running()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Missing, "Approval is required.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.RunAsync("show task progress");

        fixture.ViewModel.ResponseTitle.Should().Be("No task is running.");
        fixture.ViewModel.ResponseBody.Should().Contain("Local model inference (Ollama): NeedsAction.");
        fixture.ViewModel.ResponseBody.Should().Contain("Approval is required.");
    }

    [Fact]
    public async Task Current_task_progress_command_does_not_claim_work_when_queue_is_empty()
    {
        var fixture = new Fixture();

        await fixture.RunAsync("what is the current task status");

        fixture.ViewModel.ResponseTitle.Should().Be("No task is running.");
        fixture.ViewModel.ResponseBody.Should().Be("There is no active or pending setup task.");
    }

    [Fact]
    public async Task Approved_local_model_setup_is_tracked_and_verified_before_reporting_success()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Missing, "Model missing.");
        await fixture.ViewModel.InitializeAsync();
        fixture.LocalModel.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var install = fixture.ViewModel.InstallLocalModelAsync();
        await fixture.LocalModel.Started.Task;
        fixture.ViewModel.SetupTasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Running);
        await fixture.RunAsync("what are you currently working on");
        fixture.ViewModel.ResponseTitle.Should().Contain("Local model inference");
        await fixture.RunAsync("how far along is the current task");
        fixture.ViewModel.ResponseTitle.Should().Be("Local model inference (Ollama): running.");
        fixture.ViewModel.ResponseBody.Should().Contain("Downloading the selected local model.");
        fixture.ViewModel.ResponseBody.Should().Contain("Completion: 42%.");
        fixture.ViewModel.LocalModelSetupProgress.Should().Be(42);
        fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeFalse();

        fixture.Probe.Status = fixture.Probe.Status with
        {
            Readiness = DependencyReadiness.Ready,
            Detail = "Pinned model passed inference.",
        };
        fixture.LocalModel.Gate.SetResult();
        await install;

        fixture.ViewModel.ResponseTitle.Should().Be("Local model is ready.");
        fixture.ViewModel.SetupTasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Completed);
        fixture.LocalModel.Calls.Should().Be(1);
        fixture.ViewModel.LocalModelSetupStatus.Should().Be("Pinned model passed inference.");
        fixture.ViewModel.LocalModelSetupProgress.Should().Be(100);
        fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeFalse();
        await fixture.RunAsync("What can the local model explain?");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.Reasoner.Requests.Should().ContainSingle()
            .Which.Should().Be("What can the local model explain?");
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "local-model.install",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task Cancel_task_stops_running_model_setup_without_claiming_success()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Missing, "Model missing.");
        await fixture.ViewModel.InitializeAsync();
        fixture.LocalModel.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var install = fixture.ViewModel.InstallLocalModelAsync();
        await fixture.LocalModel.Started.Task;

        await fixture.RunAsync("cancel task");
        await install;

        fixture.ViewModel.SetupTasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Cancelled);
        fixture.ViewModel.LocalModelSetupStatus.Should().Be("Local model setup was cancelled.");
        fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeFalse();
        fixture.ViewModel.IsLocalModelSetupActive.Should().BeFalse();
        AssertAuditPair(
            fixture,
            SecurityAuditCategory.ResourceWrite,
            "local-model.install",
            SecurityAuditInitiator.LocalUser,
            SecurityAuditOutcome.Cancelled,
            "user-cancelled");
    }

    [Fact]
    public async Task Failed_model_readiness_after_install_never_reports_success()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Missing, "Still missing.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.InstallLocalModelAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Local model setup failed.");
        fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Failed);
        fixture.ViewModel.LocalModelSetupStatus.Should().StartWith("Setup failed:");
        fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeFalse();
        fixture.ViewModel.IsLocalModelSetupActive.Should().BeFalse();
        AssertAuditPair(fixture, SecurityAuditCategory.ResourceWrite, "local-model.install",
            SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Failed, "setup-failed");
    }

    [Fact]
    public async Task Refresh_during_model_or_PowerShell_setup_does_not_interrupt_installation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.LocalModel.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var modelInstall = fixture.ViewModel.InstallLocalModelAsync();
        await fixture.LocalModel.Started.Task;

        await fixture.ViewModel.DetectMicrophonesAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("Local model setup is running.");
        fixture.LocalModel.Gate.SetResult();
        await modelInstall;

        fixture.PowerShell.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var powerShellInstall = fixture.ViewModel.InstallPowerShellAsync();
        await fixture.PowerShell.Started.Task;
        await fixture.ViewModel.DetectMicrophonesAsync();
        fixture.ViewModel.ResponseTitle.Should().Be("PowerShell setup is running.");
        fixture.PowerShell.Gate.SetResult();
        await powerShellInstall;
    }

    [Fact]
    public async Task Refresh_during_an_existing_refresh_does_not_start_another_probe()
    {
        var fixture = new Fixture();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRefresh = fixture.ViewModel.DetectMicrophonesAsync();
        fixture.ViewModel.IsBusy.Should().BeTrue();

        await fixture.ViewModel.DetectMicrophonesAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("Readiness check is busy.");
        fixture.Probe.Gate.SetResult();
        await firstRefresh;
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_setup_and_busy_refresh_reject_unavailable_installations()
    {
        var fixture = new Fixture();
        fixture.Probe.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = fixture.ViewModel.DetectMicrophonesAsync();
        var busyModel = () => fixture.ViewModel.InstallLocalModelAsync();
        var busyPowerShell = () => fixture.ViewModel.InstallPowerShellAsync();
        await busyModel.Should().ThrowAsync<InvalidOperationException>();
        await busyPowerShell.Should().ThrowAsync<InvalidOperationException>();
        fixture.Probe.Gate.SetResult();
        await refreshing;

        fixture.LocalModel.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installing = fixture.ViewModel.InstallLocalModelAsync();
        await fixture.LocalModel.Started.Task;
        await busyModel.Should().ThrowAsync<InvalidOperationException>();
        await busyPowerShell.Should().ThrowAsync<InvalidOperationException>();
        fixture.LocalModel.Gate.SetResult();
        await installing;
        fixture.LocalModel.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Model_setup_failure_does_not_claim_readiness_or_leave_setup_active()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Missing, "Not installed.");
        await fixture.ViewModel.InitializeAsync();
        fixture.LocalModel.Failure = new IOException("download failed");

        await fixture.ViewModel.InstallLocalModelAsync();

        fixture.ViewModel.IsLocalModelSetupActive.Should().BeFalse();
        fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Failed);
        fixture.ViewModel.ResponseTitle.Should().Be("Local model setup failed.");
        AssertAuditPair(fixture, SecurityAuditCategory.ResourceWrite, "local-model.install",
            SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Failed, "setup-failed");
    }

    [Fact]
    public async Task Starting_model_setup_supersedes_pending_question_without_followup()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var staleChoice = fixture.ViewModel.ModelQuestionChoices[0];

        await fixture.ViewModel.InstallLocalModelAsync();
        await fixture.ViewModel.SelectModelQuestionChoiceAsync(staleChoice);

        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();
        fixture.Reasoner.Requests.Should().ContainSingle();
        fixture.ViewModel.ResponseTitle.Should().Be("Local model is ready.");
    }

    [Fact]
    public async Task Starting_PowerShell_setup_supersedes_pending_grant_without_saving_it()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();

        await fixture.ViewModel.InstallPowerShellAsync();

        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be("PowerShell 7 is ready.");
    }

    [Fact]
    public async Task Stopping_speech_during_PowerShell_setup_does_not_cancel_installation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PowerShell.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installation = fixture.ViewModel.InstallPowerShellAsync();
        await fixture.PowerShell.Started.Task;

        await fixture.RunAsync("Kora, stop speaking");

        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Running);
        fixture.ViewModel.IsPowerShellSetupActive.Should().BeTrue();

        await fixture.ViewModel.CancelCurrentTaskAsync();
        await installation;

        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Cancelled);
    }

    [Fact]
    public async Task Exiting_clears_a_pending_grant_and_question_without_executing_either()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new LocalModelQuestion("Which?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();

        await fixture.ViewModel.ExitAsync();
        fixture.ViewModel.IsModelQuestionPending.Should().BeFalse();

        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await fixture.ViewModel.ExitAsync();
        fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Exiting_clears_pending_model_action_and_session_grants()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)", DependencyReadiness.Ready, "Ready.");
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.PrepareGrantChange(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));
        await fixture.ViewModel.ConfirmGrantChangeAsync();
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.RunAsync("please lock this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;

        await fixture.ViewModel.ExitAsync();

        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Approved_PowerShell_setup_is_queued_and_verified_before_success()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PowerShell.Gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var install = fixture.ViewModel.InstallPowerShellAsync();
        await fixture.PowerShell.Started.Task;
        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Running);
        fixture.ViewModel.CanInstallLocalModel.Should().BeFalse();
        await fixture.RunAsync("what is the current task status");
        fixture.ViewModel.ResponseTitle.Should().Contain("PowerShell 7");

        fixture.PowerShell.Gate.SetResult();
        await install;

        fixture.ViewModel.ResponseTitle.Should().Be("PowerShell 7 is ready.");
        fixture.ViewModel.PowerShellSetupStatus.Should().Be("PowerShell verified.");
        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Completed);
        fixture.ViewModel.Dependencies.Should().Contain(status =>
            string.Equals(status.Id, "powershell.runtime", StringComparison.Ordinal)
            && status.Readiness == DependencyReadiness.Ready);
        AssertAuditPair(
            fixture, SecurityAuditCategory.ResourceWrite, "powershell.install",
            SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task PowerShell_verification_replaces_the_existing_dependency_status()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "powershell.runtime", "PowerShell 7 (pwsh)", DependencyReadiness.Missing, "Not ready.");
        await fixture.ViewModel.InitializeAsync();

        await fixture.ViewModel.InstallPowerShellAsync();

        fixture.ViewModel.Dependencies.Should().ContainSingle()
            .Which.Readiness.Should().Be(DependencyReadiness.Ready);
        fixture.ViewModel.ResponseTitle.Should().Be("PowerShell 7 is ready.");
    }

    [Fact]
    public async Task Cancelling_PowerShell_setup_leaves_a_cancelled_task()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PowerShell.Gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var install = fixture.ViewModel.InstallPowerShellAsync();
        await fixture.PowerShell.Started.Task;

        await fixture.RunAsync("cancel task");
        await install;

        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Cancelled);
        fixture.ViewModel.IsPowerShellSetupActive.Should().BeFalse();
        AssertAuditPair(
            fixture, SecurityAuditCategory.ResourceWrite, "powershell.install",
            SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Cancelled, "user-cancelled");
    }

    [Fact]
    public async Task Failed_PowerShell_verification_never_claims_readiness()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.PowerShell.Readiness = DependencyReadiness.Failed;

        await fixture.ViewModel.InstallPowerShellAsync();

        fixture.ViewModel.ResponseTitle.Should().Be("PowerShell setup failed.");
        fixture.ViewModel.SetupTasks.Single(task =>
                string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal))
            .State.Should().Be(SetupTaskState.Failed);
        fixture.ViewModel.Dependencies.Should().NotContain(status =>
            string.Equals(status.Id, "powershell.runtime", StringComparison.Ordinal)
            && status.Readiness == DependencyReadiness.Ready);
        AssertAuditPair(
            fixture, SecurityAuditCategory.ResourceWrite, "powershell.install",
            SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Failed, "setup-failed");
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
    public async Task ExitAsync_closes_after_reporting_audio_cleanup_failure_to_the_host()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.Voice.StopException = new InvalidOperationException("native capture is still closing");

        Func<Task> action = () => fixture.ViewModel.ExitAsync();

        await action.Should().NotThrowAsync();
        fixture.Events.Should().Contain("window.Close");
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
    public async Task ShowApplication_restores_a_hidden_presence()
    {
        var fixture = new Fixture();
        await fixture.RunAsync("Kora, hide Kora");
        fixture.WindowActions.Clear();

        fixture.ViewModel.ShowApplication();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Show);
        fixture.ViewModel.State.Should().Be(AssistantState.Information);
    }

    [Fact]
    public void Presence_interaction_requests_the_presence_and_is_safe_without_a_subscriber()
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
        fixture.WindowActions.Clear();
        fixture.ViewModel.CommandText = "Kora, hide Kora";

        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();

        fixture.WindowActions.Should().ContainSingle().Which.Should().Be(WindowAction.Hide);
        fixture.ViewModel.State.Should().Be(AssistantState.Hidden);
        fixture.ViewModel.IsListening.Should().BeFalse();
        fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
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

        await fixture.RaiseActivatedTranscriptAsync("Kora, lock the machine", 0.9f);

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

        await fixture.RaiseActivatedTranscriptAsync("Kora, show Kora", 0.87f);

        fixture.ViewModel.Transcript.Should().Contain(0.87f.ToString("P0", CultureInfo.CurrentCulture));
        fixture.WindowActions.Should().Equal(WindowAction.ShowPresence, WindowAction.Show);
    }

    [Fact]
    public async Task Recognized_voice_transcript_is_safe_without_a_window_subscriber()
    {
        var fixture = new Fixture(subscribeToWindowActions: false);

        await fixture.RaiseActivatedTranscriptAsync("Kora, what can you do", 0.87f);

        fixture.ViewModel.ResponseTitle.Should().Be("Built-in commands are ready.");
    }

    [Fact]
    public async Task Recognition_failure_is_dispatched_to_the_information_surface()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();

        fixture.Voice.RaiseFailure("not recognized");

        fixture.ViewModel.State.Should().Be(AssistantState.Information);
        fixture.ViewModel.ResponseTitle.Should().Be("Command not recognized.");
        fixture.ViewModel.ResponseBody.Should().Be("not recognized");
    }

    [Fact]
    public async Task Voice_dispatch_failure_is_reported()
    {
        var fixture = CreateVoicePrivacyFixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.BeginPushToTalkAsync();
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

    private static bool SetPresenceSetting(
        MainViewModel viewModel,
        PresenceSetting setting,
        int value,
        SecurityAuditInitiator initiator) =>
        setting switch
        {
            PresenceSetting.Size =>
                viewModel.SetPresenceSizePixels(value, initiator),
            PresenceSetting.DotSize =>
                viewModel.SetPresenceDotSizePercent(value, initiator),
            PresenceSetting.DotDensity =>
                viewModel.SetPresenceDotDensityPercent(value, initiator),
            PresenceSetting.MovementSpeed =>
                viewModel.SetPresenceMovementSpeedPercent(value, initiator),
            PresenceSetting.SpeechScaleAmount =>
                viewModel.SetPresenceSpeechScaleAmountPercent(value, initiator),
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

    private static int GetPresenceSetting(
        MainViewModel viewModel,
        PresenceSetting setting) =>
        setting switch
        {
            PresenceSetting.Size => viewModel.PresenceSizePixels,
            PresenceSetting.DotSize => viewModel.PresenceDotSizePercent,
            PresenceSetting.DotDensity => viewModel.PresenceDotDensityPercent,
            PresenceSetting.MovementSpeed => viewModel.PresenceMovementSpeedPercent,
            PresenceSetting.SpeechScaleAmount => viewModel.PresenceSpeechScaleAmountPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

    private static int? GetSavedPresenceSetting(
        FakeAppearancePreferences preferences,
        PresenceSetting setting) =>
        setting switch
        {
            PresenceSetting.Size => preferences.SavedPresenceSizePixels,
            PresenceSetting.DotSize => preferences.SavedPresenceDotSizePercent,
            PresenceSetting.DotDensity => preferences.SavedPresenceDotDensityPercent,
            PresenceSetting.MovementSpeed =>
                preferences.SavedPresenceMovementSpeedPercent,
            PresenceSetting.SpeechScaleAmount =>
                preferences.SavedPresenceSpeechScaleAmountPercent,
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

    public enum PresenceSetting
    {
        Size,
        DotSize,
        DotDensity,
        MovementSpeed,
        SpeechScaleAmount,
    }

    private sealed class FailedEvidenceLogger : ILogger<MainViewModel>
    {
        public bool Fail { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Fail)
            {
                throw new IOException("fixture logging failure");
            }
        }
    }

    private sealed class QueryTaskStore : IHostTaskStore
    {
        public List<HostTaskRecord> Records { get; } = [];
        public Action<HostTaskRecord>? BeforeCommit { get; set; }

        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeCommit?.Invoke(record);
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<HostTaskRecord>>([]);
    }

    private sealed class Fixture
    {
        public Fixture(bool subscribeToWindowActions = true, ILogger<MainViewModel>? logger = null,
            Kora.Application.Voice.BoundedMicrophoneCatalog? microphoneCatalog = null, bool enableOutputConfiguration = false,
            bool enablePlaybackVolume = false, Exception? volumeReadFailure = null)
        {
            Catalog = new BuiltInCommandCatalog();
            Dispatcher = new ImmediateDispatcher();
            Voice = new FakeVoiceRecognitionService(Dispatcher, Events);
            TextToSpeech = new FakeTextToSpeechService(Events);
            NamePreferences = new FakeAssistantNamePreferences();
            AppearancePreferences = new FakeAppearancePreferences();
            MicrophoneAccess = new FakeMicrophoneAccessService();
            Preferences = new FakeTextToSpeechPreferences();
            SpeechOffers = new FakeSpeechOfferPreferences();
            AudioPreferences = new FakeAudioDevicePreferences();
            var audioStore = new Kora.Application.UnitTests.Configuration.AudioControlTestStore();
            OutputAdmission = new(audioStore, audioStore, new HostTaskCoordinator(audioStore));
            OutputConfiguration = enableOutputConfiguration ? new(
                AudioPreferences, new Kora.Application.Voice.BoundedAudioOutputCatalog(
                    TextToSpeech, NullLogger<Kora.Application.Voice.BoundedAudioOutputCatalog>.Instance),
                OutputAdmission, Audit = new FakeSecurityAuditLog(), TextToSpeech) : null;
            VolumePreferences = new() { ReadFailure = volumeReadFailure };
            VolumeConfiguration = enablePlaybackVolume ? new(VolumePreferences, TextToSpeech, OutputAdmission, Audit ??= new FakeSecurityAuditLog()) : null;
            OutputPreferences = new FakeResponseOutputPreferences();
            CallPreferences = new FakeCallAwarePreferences();
            CallState = new FakeCallStateService();
            Session = new FakeSessionController(Events);
            PrivacyObservation = new FakePrivacyObservationService(() => new WindowsPrivacySnapshot(
                Session.IsUnlocked ? WindowsSessionState.Unlocked : WindowsSessionState.Locked,
                MicrophoneAccess.Status.State, 0,
                Voice.Microphones.Select(device => device.Id).ToArray(),
                Voice.DefaultMicrophoneId, TextToSpeech.DefaultOutputDeviceId));
            Process = new FakeApplicationProcessController(Events);
            Audit ??= new FakeSecurityAuditLog();
            Probe = new StubProbe(new DependencyStatus(
                "storage",
                "Storage",
                DependencyReadiness.Ready,
                "ready"));
            var bootstrapper = new DependencyBootstrapper(
                [Probe],
                NullLogger<DependencyBootstrapper>.Instance);
            LocalModel = new FakeLocalModelSetup();
            PowerShell = new FakePowerShellSetup();
            Reasoner = new FakeLocalModelReasoner();
            ApprovalPreferences = new FakeModelApprovalPreferences();
            ModelExecutionPreferences = new FakeModelExecutionPreferences();
            ApplicationInfo = new FakeApplicationInfo();
            var clipboard = new Kora.Tools.Clipboard.ClipboardSnapshotBroker(ClipboardReader, TimeProvider.System,
                NullLogger<Kora.Tools.Clipboard.ClipboardSnapshotBroker>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
            var artifactCatalogue = new ArtifactCatalogue(
            [
                new ArtifactDefinition(
                    "kora.session.lock",
                    ArtifactKind.Skill,
                    "Lock the machine",
                    "Lock the current Windows session.",
                    "lock",
                    ["lock", "lock the machine"],
                    "bundled",
                    "1.0.0",
                    new string('0', 64),
                    "Select only for an explicit request to lock the current session."),
            ]);
            ViewModel = new MainViewModel(
                Catalog,
                new BuiltInCommandRouter(Catalog),
                artifactCatalogue,
                new ArtifactCommandRouter(artifactCatalogue),
                bootstrapper,
                new DependencySetupWorkflow(
                    bootstrapper,
                    LocalModel,
                    PowerShell),
                Reasoner,
                ApprovalPreferences,
                ModelExecutionPreferences,
                MicrophoneAccess,
                Voice,
                TextToSpeech,
                new AssistantNameConfigurationService(NamePreferences, Catalog, Audit,
                    NullLogger<AssistantNameConfigurationService>.Instance),
                AppearancePreferences,
                SpeechOffers,
                AudioPreferences,
                OutputPreferences,
                CallPreferences,
                CallState,
                Session,
                Process,
                Dispatcher,
                UserName,
                ApplicationInfo,
                Audit,
                logger ?? NullLogger<MainViewModel>.Instance,
                VoiceConsent,
                PrivacyObservation,
                new DurableVersionQuery(new HostTaskCoordinator(HostStore), Audit,
                    NullLogger<DurableVersionQuery>.Instance),
                new Kora.Application.Tools.ReadOnlyCapabilityRegistry(
                    new CapabilityHostAccess(Session),
                    new Kora.Tools.Capabilities.CapabilitiesList(),
                    new Kora.Tools.Capabilities.CapabilitiesGet(),
                    new Kora.Tools.Application.ApplicationGetVersion(ApplicationInfo),
                    new Kora.Tools.Readiness.ReadinessGet(bootstrapper),
                    new Kora.Tools.Runtime.RuntimeList(runtime),
                    new Kora.Tools.Runtime.RuntimeGetStatus(runtime),
                    NullLogger<Kora.Application.Tools.ReadOnlyCapabilityRegistry>.Instance),
                new AppearanceConfigurationService(AppearancePreferences, Audit,
                    NullLogger<AppearanceConfigurationService>.Instance),
                new SpeechConfigurationService(Preferences, TextToSpeech, Audit,
                    NullLogger<SpeechConfigurationService>.Instance),
                clipboard,
                new Kora.Tools.Clipboard.ClipboardRead(clipboard),
                new Kora.Tools.Clipboard.ClipboardReuse(clipboard),
                new Kora.Tools.Clipboard.ClipboardRevoke(clipboard),
                microphoneCatalog, OutputConfiguration, VolumeConfiguration);
            ViewModel.BindCallOwnershipGate(static () => true);
            ViewModel.BindClipboardOwnershipGate(static () => true);
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

        private sealed class CapabilityHostAccess(FakeSessionController session) : Kora.Core.Tools.ICapabilityHostAccess
        {
            public bool IsCurrentHost => session.IsUnlocked;
        }

        public QueryTaskStore HostStore { get; } = new();
        public Kora.Application.Voice.AudioControlAdmission OutputAdmission { get; }
        public OutputDeviceConfigurationService? OutputConfiguration { get; }
        public FakePlaybackVolumePreferences VolumePreferences { get; }
        public PlaybackVolumeConfigurationService? VolumeConfiguration { get; }

        public ImmediateDispatcher Dispatcher { get; }

        public FakeVoiceRecognitionService Voice { get; }

        public FakeTextToSpeechService TextToSpeech { get; }

        public FakeAssistantNamePreferences NamePreferences { get; }

        public FakeAppearancePreferences AppearancePreferences { get; }

        public FakeMicrophoneAccessService MicrophoneAccess { get; }

        public FakeTextToSpeechPreferences Preferences { get; }

        public FakeSpeechOfferPreferences SpeechOffers { get; }

        public FakeAudioDevicePreferences AudioPreferences { get; }

        public FakeResponseOutputPreferences OutputPreferences { get; }

        public FakeCallAwarePreferences CallPreferences { get; }

        public FakeCallStateService CallState { get; }

        public FakeSessionController Session { get; }

        public FakeApplicationProcessController Process { get; }

        public FakeSecurityAuditLog Audit { get; }

        public StubProbe Probe { get; }

        public FakeLocalModelSetup LocalModel { get; }

        public FakePowerShellSetup PowerShell { get; }

        public FakeLocalModelReasoner Reasoner { get; }

        public FakeModelApprovalPreferences ApprovalPreferences { get; }

        public FakeModelExecutionPreferences ModelExecutionPreferences { get; }

        public FakeApplicationInfo ApplicationInfo { get; }

        public FakeCurrentUserNameProvider UserName { get; } = new();

        public MainViewModel ViewModel { get; }

        public FakeVoiceConsentPreferences VoiceConsent { get; } = new();

        public FakePrivacyObservationService PrivacyObservation { get; }

        public sealed class FakePrivacyObservationService(Func<WindowsPrivacySnapshot> read) : IWindowsPrivacyObservationService
        {
            private WindowsPrivacySnapshot? snapshotOverride;

            public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;

            public WindowsPrivacySnapshot Current
            {
                get => snapshotOverride ?? read();
                set => snapshotOverride = value;
            }

            public Action? BeforeRefresh { get; set; }

            public Exception? RefreshException { get; set; }

            public WindowsPrivacySnapshot Refresh()
            {
                var callback = BeforeRefresh;
                BeforeRefresh = null;
                callback?.Invoke();
                if (RefreshException is { } exception)
                {
                    throw exception;
                }
                return Current;
            }

            public void Publish(WindowsPrivacyChangedEventArgs change) => Changed?.Invoke(this, change);

            public Action<WindowsPrivacyChangedEventArgs> CapturePublisher()
            {
                var handlers = Changed;
                return change => handlers?.Invoke(this, change);
            }

            public void Dispose()
            {
            }
        }

        public List<WindowAction> WindowActions { get; } = [];

        public List<string> Events { get; } = [];

        public async Task RaiseActivatedTranscriptAsync(string transcript, float confidence)
        {
            if (Voice.Microphones.Count == 0)
            {
                Voice.Microphones = [new MicrophoneDevice("0", "Headset")];
                Voice.DefaultMicrophoneId = "0";
            }
            await ViewModel.RefreshMicrophonesAsync();
            ViewModel.SelectedMicrophone ??= SystemAudioDevices.Microphone;
            if (!ViewModel.HasVoiceConsent)
            {
                await ViewModel.SetVoiceConsentAsync(true);
            }
            if (!ViewModel.IsVoiceEnabled)
            {
                await ViewModel.ToggleListeningCommand.ExecuteAsync();
            }
            await ViewModel.BeginPushToTalkAsync();
            await ViewModel.EndPushToTalkAsync();
            WindowActions.Clear();
            await Voice.RaiseTranscriptAsync(transcript, confidence);
        }

        public sealed class FakeVoiceConsentPreferences : IVoiceConsentPreferences
        {
            public bool? Consent { get; set; } = true;

            public Exception? Failure { get; set; }

            public bool? Load() => Failure is { } error ? throw error : Consent;

            public void Save(bool consent)
            {
                if (Failure is { } error)
                {
                    throw error;
                }
                Consent = consent;
            }
        }

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

        public sealed class FakeSpeechOfferPreferences : IOptionalSpeechOfferPreferences
        {
            public OptionalSpeechOfferState State { get; private set; } = new(false, null);

            public IOException? Failure { get; set; }

            public OptionalSpeechOfferState Load() =>
                Failure is { } exception ? throw exception : State;

            public void Save(OptionalSpeechOfferState state)
            {
                if (Failure is not null)
                {
                    throw Failure;
                }
                State = state;
            }
        }

        public sealed class FakeLocalModelSetup : ILocalModelSetup
        {
            public TaskCompletionSource? Gate { get; set; }

            public Exception? Failure { get; set; }

            public TaskCompletionSource Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Calls { get; private set; }

            public async Task InstallAsync(
                IProgress<LocalModelSetupProgress> progress,
                CancellationToken cancellationToken)
            {
                Calls++;
                progress.Report(new LocalModelSetupProgress("Downloading the selected local model.", 42));
                Started.TrySetResult();
                if (Gate is not null)
                {
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                if (Failure is not null)
                {
                    throw Failure;
                }
            }
        }

        public sealed class FakePowerShellSetup : IPowerShellSetup
        {
            public string TaskId => "powershell.runtime";

            public string TaskName => "PowerShell 7 (pwsh)";

            public int InstallCalls { get; private set; }

            public TaskCompletionSource? Gate { get; set; }

            public TaskCompletionSource Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Exception? Failure { get; set; }

            public DependencyReadiness Readiness { get; set; } = DependencyReadiness.Ready;

            public async Task InstallAsync(CancellationToken cancellationToken)
            {
                InstallCalls++;
                Started.TrySetResult();
                if (Gate is not null)
                {
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                if (Failure is not null)
                {
                    throw Failure;
                }
            }

            public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
                ValueTask.FromResult(new DependencyStatus(
                    TaskId, TaskName, Readiness, "PowerShell verified."));
        }

        public sealed class FakeLocalModelReasoner : ILocalModelReasoner
        {
            public List<string> Requests { get; } = [];

            public TaskCompletionSource<string>? Gate { get; set; }

            public BuiltInAction? Action { get; set; }
            public GrantChange? GrantChange { get; set; }
            public LocalModelQuestion? Question { get; set; }

            public LocalModelResponse? Response { get; set; }

            public Exception? Failure { get; set; }

            public LocalModelContext? LastContext { get; private set; }
            public LocalModelArtifact? LastArtifact { get; private set; }

            public async Task<LocalModelResponse> ReasonAsync(
                string request,
                LocalModelContext context,
                LocalModelArtifact? artifact,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                LastContext = context;
                LastArtifact = artifact;
                if (Failure is not null)
                {
                    throw Failure;
                }

                if (Response is not null)
                {
                    return Response;
                }
                if (Action is not null)
                {
                    return new LocalModelResponse(null, Action);
                }
                if (GrantChange is not null)
                {
                    return new LocalModelResponse(null, null, GrantChange);
                }
                if (Question is not null)
                {
                    return new LocalModelResponse(null, null, null, Question);
                }

                return new LocalModelResponse(
                    Gate is null ? "A local answer." : await Gate.Task.WaitAsync(cancellationToken),
                    null);
            }
        }

        public sealed class FakeModelApprovalPreferences : IModelApprovalPreferences
        {
            public ModelApprovalPreferences Preferences { get; private set; } =
                new(true, []);

            public IOException? SaveFailure { get; set; }

            public ModelApprovalPreferences Load() => Preferences;

            public void Save(ModelApprovalPreferences preferences)
            {
                if (SaveFailure is not null)
                {
                    throw SaveFailure;
                }

                Preferences = preferences;
            }
        }

        public sealed class FakeModelExecutionPreferences : IModelExecutionPreferences
        {
            public ModelExecutionSettings Settings { get; private set; } =
                ModelExecutionSettings.Default;

            public IOException? SaveFailure { get; set; }

            public ModelExecutionSettings Load() => Settings;

            public void Save(ModelExecutionSettings settings)
            {
                if (SaveFailure is not null)
                {
                    throw SaveFailure;
                }

                Settings = settings;
            }
        }

        public FakeClipboardReader ClipboardReader { get; } = new();

        public async Task RunAsync(string command)
        {
            ViewModel.CommandText = command;
            await ViewModel.RunTypedCommand.ExecuteAsync();
        }
    }

    private sealed class StubProbe(DependencyStatus status) : ISetupDependencyProbe
    {
        public DependencyStatus Status { get; set; } = status;

        public string TaskId => Status.Id;

        public string TaskName => Status.Name;

        public TaskCompletionSource? Gate { get; set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Status;
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

        public Action? BeforeInvoke { get; set; }

        public Action? BeforePost { get; set; }

        public Task InvokeAsync(Func<Task> action)
        {
            if (InvokeException is not null)
            {
                throw InvokeException;
            }

            var callback = BeforeInvoke;
            BeforeInvoke = null;
            callback?.Invoke();
            LastInvocation = action();
            return LastInvocation;
        }

        public void Post(Action action)
        {
            var callback = BeforePost;
            BeforePost = null;
            callback?.Invoke();
            action();
        }
    }

    private sealed class FakeVoiceRecognitionService(
        ImmediateDispatcher dispatcher,
        List<string> events) : IActivatedVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;

        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted;

        public bool IsListening { get; private set; }

        public long Generation { get; private set; }

        public long CaptureGeneration => Generation;

        public bool IsAmbientListeningAvailable => false;

        public bool IsCaptureQuiescent => !IsListening && !PreventQuiescence;

        public bool PreventQuiescence { get; set; }

        public bool RejectAcknowledgement { get; set; }

        public string? EarlyTranscript { get; set; }

        public bool AcceptCaptureGeneration(long generation)
        {
            if (RejectAcknowledgement || generation != Generation)
            {
                return false;
            }
            if (EarlyTranscript is { } transcript)
            {
                EarlyTranscript = null;
                RaiseTranscript(transcript, 1);
            }
            return true;
        }

        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            string? assistantName = null, CancellationToken cancellationToken = default) =>
            StartAsync(microphone, phrases, assistantName, cancellationToken);

        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) =>
            EndCaptureAsync(cancellationToken);

        public void PublishCaptureState(VoiceCaptureStateChangedEventArgs change) =>
            CaptureStateChanged?.Invoke(this, change);

        public void CompleteCapture() => IsListening = false;

        public void PublishCompletion(VoiceRecognitionCompletedEventArgs change) =>
            RecognitionCompleted?.Invoke(this, change);

        public void InvalidateCapture()
        {
            Generation++;
            IsListening = false;
        }

        public Task EndCaptureAsync(CancellationToken cancellationToken = default)
        {
            if (EndException is not null)
            {
                throw EndException;
            }
            IsListening = false;
            return Task.CompletedTask;
        }

        public IReadOnlyList<MicrophoneDevice> Microphones { get; set; } = [];

        public Exception? GetMicrophonesException { get; set; }
        public Action? BeforeEnumeration { get; set; }

        public Exception? StartException { get; set; }

        public Exception? StopException { get; set; }

        public Exception? EndException { get; set; }

        public Action? BeforeStopFailure { get; set; }

        public TaskCompletionSource? StartGate { get; set; }

        public bool IgnoreStartCancellation { get; set; }

        public Action? AfterStart { get; set; }

        public void AdvanceGeneration() => Generation++;

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
            BeforeEnumeration?.Invoke();
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
            string? assistantName = null,
            CancellationToken cancellationToken = default)
        {
            StartedMicrophone = microphone;
            StartedPhrases = phrases.ToArray();
            StartCalls++;
            Generation++;
            if (StartException is not null)
            {
                throw StartException;
            }

            if (StartGate is not null)
            {
                if (IgnoreStartCancellation)
                {
                    await StartGate.Task;
                }
                else
                {
                    await StartGate.Task.WaitAsync(cancellationToken);
                }
            }

            IsListening = true;
            if (EarlyTranscript is not null)
            {
                IsListening = false;
            }
            AfterStart?.Invoke();
        }

        public TaskCompletionSource? StopGate { get; set; }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("voice.stop");
            StopCalls++;
            BeforeStopFailure?.Invoke();
            if (StopException is not null)
            {
                throw StopException;
            }
            IsListening = false;
            if (StopGate is not null) { await StopGate.Task.WaitAsync(cancellationToken); }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task RaiseTranscriptAsync(string transcript, float confidence)
        {
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence, Generation));
            await dispatcher.LastInvocation;
        }

        public void RaiseTranscript(string transcript, float confidence) =>
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs(transcript, confidence, Generation));

        public void RaiseTranscriptForGeneration(long generation) =>
            TranscriptRecognized?.Invoke(this, new VoiceTranscriptEventArgs("lock the machine", 1, generation));

        public void RaiseFailure(string message) =>
            RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs(message, Generation));

        public void RaiseRetiredFailure(string message)
        {
            var retired = Generation;
            InvalidateCapture();
            RecognitionFailed?.Invoke(this, new VoiceRecognitionFailureEventArgs(message, retired));
        }
    }

    private sealed class FakeTextToSpeechService(List<string> events) : ITextToSpeechService, IPlaybackVolumeControl
    {
        private long outputGeneration;
        public PlaybackVolume? Volume { get; private set; } = PlaybackVolume.Default;
        public void SetPlaybackVolume(PlaybackVolume? volume)
        {
            Volume = volume;
            InvalidateOutput();
        }
        public void InvalidateOutput()
        {
            Interlocked.Increment(ref outputGeneration);
            events.Add("speech.invalidate");
            IsSpeaking = false;
            PlaybackFrame = SpeechPlaybackFrame.Inactive;
            PlaybackFrameException = null;
        }

        public bool IsSpeaking { get; private set; }

        private SpeechPlaybackFrame playbackFrame = SpeechPlaybackFrame.Inactive;

        public AudioOutputDeviceUnavailableException? PlaybackFrameException { get; set; }

        public SpeechPlaybackFrame PlaybackFrame
        {
            get => PlaybackFrameException is { } exception ? throw exception : playbackFrame;
            set => playbackFrame = value;
        }

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

        public Action? BeforeSpeak { get; set; }

        public Exception? StopException { get; set; }

        public Action? BeforeStop { get; set; }

        public Exception? OutputEnumerationException { get; set; }

        public TaskCompletionSource? StopGate { get; set; }

        public TaskCompletionSource StopStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? SpeakGate { get; set; }

        public bool HoldSpeechCompletionOnStop { get; set; }

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
            var generation = Interlocked.Read(ref outputGeneration);
            BeforeSpeak?.Invoke();
            if (generation != Interlocked.Read(ref outputGeneration)) { throw new OperationCanceledException(); }
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
            if (generation != Interlocked.Read(ref outputGeneration)) { throw new OperationCanceledException(); }

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

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("tts.stop");
            StopCalls++;
            StopStarted.TrySetResult();
            BeforeStop?.Invoke();
            if (StopException is not null)
            {
                throw StopException;
            }
            IsSpeaking = false;
            if (!HoldSpeechCompletionOnStop)
            {
                SpeakGate?.TrySetResult();
            }
            if (StopGate is not null)
            {
                await StopGate.Task.WaitAsync(cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void ClearSpokenResponse()
        {
            SpokenText = null;
            SpokenVoice = null;
            SpokenOutputDevice = null;
        }

        public Action? BeforeOutputEnumeration { get; set; }
        public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
        {
            var callback = BeforeOutputEnumeration;
            BeforeOutputEnumeration = null;
            callback?.Invoke();
            return OutputEnumerationException is { } exception ? throw exception : OutputDevices;
        }

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

        public int? ResponseTimeoutSeconds { get; set; }

        public int? PresenceSizePixels { get; set; }

        public int? PresenceDotSizePercent { get; set; }

        public int? PresenceDotDensityPercent { get; set; }

        public int? PresenceMovementSpeedPercent { get; set; }

        public bool? PresenceSpeechScalingEnabled { get; set; }

        public int? PresenceSpeechScaleAmountPercent { get; set; }

        public PresencePosition? PresencePosition { get; set; }

        public ResponseWindowSettings? ResponseWindowSettings { get; set; }

        public ApplicationThemeMode? SavedMode { get; private set; }

        public int? SavedPresenceTimeoutSeconds { get; private set; }

        public int? SavedResponseTimeoutSeconds { get; private set; }

        public int? SavedPresenceSizePixels { get; private set; }

        public int? SavedPresenceDotSizePercent { get; private set; }

        public int? SavedPresenceDotDensityPercent { get; private set; }

        public int? SavedPresenceMovementSpeedPercent { get; private set; }

        public bool? SavedPresenceSpeechScalingEnabled { get; private set; }

        public int? SavedPresenceSpeechScaleAmountPercent { get; private set; }

        public PresencePosition? SavedPresencePosition { get; private set; }

        public ResponseWindowSettings? SavedResponseWindowSettings { get; private set; }

        public Exception? LoadException { get; set; }

        public Exception? PresenceTimeoutLoadException { get; set; }

        public Exception? ResponseTimeoutLoadException { get; set; }

        public Exception? DotDensityLoadException { get; set; }

        public Exception? SpeechScalingLoadException { get; set; }

        public Exception? SpeechScaleAmountLoadException { get; set; }

        public Exception? SaveException { get; set; }

        public Action? BeforePresencePreferenceSave { get; set; }

        public Action? BeforeResponseTimeoutPreferenceSave { get; set; }

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
            if (PresenceTimeoutLoadException is not null)
            {
                throw PresenceTimeoutLoadException;
            }

            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceTimeoutSeconds;
        }

        public void SavePresenceTimeoutSeconds(int seconds)
        {
            ThrowIfSaveFails();
            SavedPresenceTimeoutSeconds = seconds;
            PresenceTimeoutSeconds = seconds;
        }

        public int? LoadResponseTimeoutSeconds()
        {
            if (ResponseTimeoutLoadException is not null)
            {
                throw ResponseTimeoutLoadException;
            }

            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ResponseTimeoutSeconds;
        }

        public void SaveResponseTimeoutSeconds(int seconds)
        {
            BeforeResponseTimeoutPreferenceSave?.Invoke();
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedResponseTimeoutSeconds = seconds;
            ResponseTimeoutSeconds = seconds;
        }

        public int? LoadPresenceSizePixels()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceSizePixels;
        }

        public int? LoadPresenceDotSizePercent()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceDotSizePercent;
        }

        public int? LoadPresenceDotDensityPercent()
        {
            if (DotDensityLoadException is not null)
            {
                throw DotDensityLoadException;
            }

            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceDotDensityPercent;
        }

        public int? LoadPresenceMovementSpeedPercent()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceMovementSpeedPercent;
        }

        public bool? LoadPresenceSpeechScalingEnabled()
        {
            if (SpeechScalingLoadException is not null)
            {
                throw SpeechScalingLoadException;
            }

            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceSpeechScalingEnabled;
        }

        public int? LoadPresenceSpeechScaleAmountPercent()
        {
            if (SpeechScaleAmountLoadException is not null)
            {
                throw SpeechScaleAmountLoadException;
            }

            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresenceSpeechScaleAmountPercent;
        }

        public PresencePosition? LoadPresencePosition()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return PresencePosition;
        }

        public ResponseWindowSettings? LoadResponseWindowSettings()
        {
            if (LoadException is not null)
            {
                throw LoadException;
            }

            return ResponseWindowSettings;
        }

        public void SavePresenceSizePixels(int value)
        {
            ThrowIfSaveFails();
            SavedPresenceSizePixels = value;
            PresenceSizePixels = value;
        }

        public void SavePresenceDotSizePercent(int value)
        {
            ThrowIfSaveFails();
            SavedPresenceDotSizePercent = value;
            PresenceDotSizePercent = value;
        }

        public void SavePresenceDotDensityPercent(int value)
        {
            ThrowIfSaveFails();
            SavedPresenceDotDensityPercent = value;
            PresenceDotDensityPercent = value;
        }

        public void SavePresenceMovementSpeedPercent(int value)
        {
            ThrowIfSaveFails();
            SavedPresenceMovementSpeedPercent = value;
            PresenceMovementSpeedPercent = value;
        }

        public void SavePresenceSpeechScalingEnabled(bool value)
        {
            ThrowIfSaveFails();
            SavedPresenceSpeechScalingEnabled = value;
            PresenceSpeechScalingEnabled = value;
        }

        public void SavePresenceSpeechScaleAmountPercent(int value)
        {
            ThrowIfSaveFails();
            SavedPresenceSpeechScaleAmountPercent = value;
            PresenceSpeechScaleAmountPercent = value;
        }

        public void SavePresencePosition(PresencePosition position)
        {
            ThrowIfSaveFails();
            SavedPresencePosition = position;
            PresencePosition = position;
        }

        public void SaveResponseWindowSettings(ResponseWindowSettings settings)
        {
            ThrowIfSaveFails();
            SavedResponseWindowSettings = settings;
            ResponseWindowSettings = settings;
        }

        private void ThrowIfSaveFails()
        {
            BeforePresencePreferenceSave?.Invoke();
            if (SaveException is not null)
            {
                throw SaveException;
            }
        }
    }

    private sealed class FakeTextToSpeechPreferences : ITextToSpeechPreferences
    {
        public SpokenSummaryLimits? SummaryLimits { get; set; }
        public Exception? SummaryLimitsLoadFailure { get; set; }
        public SpokenSummaryLimits? LoadSummaryLimits() => SummaryLimitsLoadFailure is { } exception ? throw exception : SummaryLimits;
        public void SaveSummaryLimits(SpokenSummaryLimits limits)
        {
            if (SaveException is not null) { throw SaveException; }
            SummaryLimits = limits;
        }
        public SpeechSelection? LoadSelection() => ProviderId is null && VoiceId is null
            ? null : new(ProviderId ?? SpeechProviderIds.Windows, VoiceId);

        public void SaveSelection(SpeechSelection selection)
        {
            if (ProviderSaveException is not null) { throw ProviderSaveException; }
            if (SaveException is not null) { throw SaveException; }
            SavedProviderId = selection.ProviderId;
            SavedVoiceId = selection.VoiceId;
            ProviderId = selection.ProviderId;
            VoiceId = selection.VoiceId;
        }

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

        public bool? MutedOutputVisualFallback { get; set; }

        public bool? SavedMutedOutputVisualFallback { get; private set; }

        public Exception? SaveException { get; set; }

        public ResponseOutputMode? LoadDefaultMode() => Mode;

        public bool? LoadMutedOutputVisualFallback() => MutedOutputVisualFallback;

        public void SaveMutedOutputVisualFallback(bool enabled)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            SavedMutedOutputVisualFallback = enabled;
            MutedOutputVisualFallback = enabled;
        }

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

        public void SetInvalidObservation()
        {
            CurrentState = (CallState)99;
            StateChanged?.Invoke(this, new CallStateChangedEventArgs(CallState.Unknown));
        }

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

        public Exception? OpenMicrophoneSettingsException { get; set; }

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

        public Action<SecurityAuditEvent>? BeforeWrite { get; set; }

        public void Write(SecurityAuditEvent auditEvent)
        {
            BeforeWrite?.Invoke(auditEvent);
            Events.Add(auditEvent);
        }
    }

    private sealed class FakeApplicationInfo : IApplicationInfo
    {
        public IOException? Failure { get; set; }

        public string Version => Failure is { } exception ? throw exception : "1.2.3";
    }

    public sealed class FakeCurrentUserNameProvider : ICurrentUserNameProvider
    {
        public string? AddressName { get; set; } = "Rory";

        public string? GetAddressName() => AddressName;
    }
}