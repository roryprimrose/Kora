using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_exact_typed_and_current_name_activated_settings_share_admission_without_model_audio_or_execution()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.QueuePendingChoices.Should().Equal(Enumerable.Range(1, 10));
        f.ViewModel.QueueSlotChoices.Should().Equal(1, 2);
        f.ViewModel.QueueLifetimeMinuteChoices.Should().Equal(Enumerable.Range(1, 120));
        f.ViewModel.SelectedQueueLifetimeMinutes.Should().Be(30);
        f.ViewModel.CanChangeQueueConfigurationNative.Should().BeFalse();
        await f.ViewModel.SaveQueuePendingCommand.ExecuteAsync();
        f.QueuePreferences.Writes.Should().Be(0);
        f.ViewModel.BindQueueConfigurationNativeLifetime(() => true);
        f.ViewModel.CanChangeQueueConfigurationNative.Should().BeTrue();
        f.ViewModel.QueueConfigurationStatus.Should().Contain("\"available\":true");
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        f.ViewModel.SelectedQueuePending = 1;
        await f.ViewModel.SaveQueuePendingCommand.ExecuteAsync();
        f.QueuePreferences.Value.PendingPerSession.Should().Be(1);
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        f.ViewModel.SelectedQueueSlots = 2;
        await f.ViewModel.SaveQueueSlotsCommand.ExecuteAsync();
        f.QueuePreferences.Value.Should().Be(new SessionQueuePreferences(1, 2));
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        await f.ViewModel.ResetQueuePendingCommand.ExecuteAsync();
        f.QueuePreferences.Value.Should().Be(new SessionQueuePreferences(null, 2));
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        await f.ViewModel.ResetQueueSlotsCommand.ExecuteAsync();
        f.QueuePreferences.Value.IsDefault.Should().BeTrue();
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        f.ViewModel.SelectedQueueLifetimeMinutes = 120;
        await f.ViewModel.SaveQueueLifetimeCommand.ExecuteAsync();
        f.QueuePreferences.Value.PendingLifetimeMinutes.Should().Be(120);
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        await f.ViewModel.ResetQueueLifetimeCommand.ExecuteAsync();
        f.QueuePreferences.Value.IsDefault.Should().BeTrue();
        await f.RunAsync("set queue.pending-lifetime-minutes to 1");
        f.QueuePreferences.Value.PendingLifetimeMinutes.Should().Be(1);
        await f.RunAsync("status queue.pending-lifetime-minutes");
        f.ViewModel.ResponseBody.Should().Contain("\"pendingLifetimeMinutes\":1");
        await f.RunAsync("reset queue.pending-lifetime-minutes");
        await f.RunAsync("list queue settings");
        f.ViewModel.ResponseBody.Should().Contain("queue.execution-slots").And.Contain("queue.pending-per-session");
        await f.RunAsync("set queue.pending-per-session to 10");
        await f.RunAsync("status queue.pending-per-session");
        f.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        await f.RunAsync("reset queue.pending-per-session");
        await f.RunAsync("set assistant.name to Nova");
        var starts = f.Voice.StartCalls;
        f.TextToSpeech.ClearSpokenResponse();
        await f.RaiseActivatedTranscriptAsync("Nova, set queue.execution-slots to 2", 1);
        f.QueuePreferences.Value.ExecutionSlots.Should().Be(2);
        await f.RaiseActivatedTranscriptAsync("Nova, reset queue.execution-slots", 1);
        f.QueuePreferences.Value.IsDefault.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(starts + 2);
        await f.RaiseActivatedTranscriptAsync("Nova, set queue.pending-lifetime-minutes to 30", 1);
        f.QueuePreferences.Value.PendingLifetimeMinutes.Should().Be(30);
        await f.RaiseActivatedTranscriptAsync("Nova, reset queue.pending-lifetime-minutes", 1);
        f.QueuePreferences.Value.IsDefault.Should().BeTrue();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
        configuration.HoldUnavailable();
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("reopen")]
    [InlineData("call")]
    [InlineData("write")]
    [InlineData("late-lifetime")]
    public async Task Native_draft_revision_lifetime_call_and_atomic_failures_never_claim_success(string stage)
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindQueueConfigurationNativeLifetime(() => true);
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        f.ViewModel.SelectedQueuePending = 1;
        if (stage is "stale") { await f.RunAsync("set queue.execution-slots to 2"); }
        if (stage is "reopen")
        {
            f.ViewModel.BindQueueConfigurationNativeLifetime(() => false);
            f.ViewModel.BindQueueConfigurationNativeLifetime(() => true);
        }
        if (stage is "call") { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        if (stage is "write") { f.QueuePreferences.WriteFailure = new IOException("atomic save failed"); }
        if (stage is "late-lifetime") { f.QueuePreferences.AfterWrite = () => f.ViewModel.BindQueueConfigurationNativeLifetime(() => false); }
        await f.ViewModel.SaveQueuePendingCommand.ExecuteAsync();
        configuration.Get().Available.Should().BeFalse();
        f.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        if (stage is "late-lifetime") { f.QueuePreferences.Pending.Should().BeTrue(); }
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_and_corrupt_queue_preferences_are_explicit_and_invalid_values_cannot_expand_the_profile(bool corrupt)
    {
        var f = new Fixture(enableQueueConfiguration: corrupt, queueReadFailure: corrupt ? new InvalidDataException("corrupt") : null);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("get queue.execution-slots");
        if (!corrupt)
        {
            f.ViewModel.QueueConfigurationStatus.Should().Contain("unavailable");
            f.ViewModel.CanChangeQueueConfigurationNative.Should().BeFalse();
            f.ViewModel.Dispose();
            return;
        }
        await using var configuration = f.QueueConfiguration!;
        f.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        f.QueuePreferences.ReadFailure = null;
        await f.RunAsync("set queue.execution-slots to 3");
        f.ViewModel.ResponseTitle.Should().Contain("Choose queue");
        await f.RunAsync("set queue.pending-per-session to 50");
        await f.RunAsync("set queue.pending-lifetime-minutes to 121");
        f.ViewModel.ResponseTitle.Should().Contain("Choose queue");
        await f.RunAsync("set queue.active-deadline-minutes to 60");
        f.ViewModel.ResponseTitle.Should().Contain("Clarify");
        f.QueuePreferences.Writes.Should().Be(0);
        var badPending = () => f.ViewModel.SelectedQueuePending = 11;
        badPending.Should().Throw<ArgumentOutOfRangeException>();
        var badSlots = () => f.ViewModel.SelectedQueueSlots = 3;
        badSlots.Should().Throw<ArgumentOutOfRangeException>();
        var badLifetime = () => f.ViewModel.SelectedQueueLifetimeMinutes = 121;
        badLifetime.Should().Throw<ArgumentOutOfRangeException>();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Unknown_current_name_and_missing_exact_option_or_value_never_route_a_mutation()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        f.NamePreferences.Name = "Supported Commands";
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Kora, list queue settings");
        f.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        await f.RunAsync("get queue.execution-slots");
        f.ViewModel.ResponseBody.Should().Contain("queue.execution-slots");
        await f.ViewModel.ExecuteQueueConfigurationCommandAsync(new(AppearanceCommandOperation.Set),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        f.ViewModel.ResponseTitle.Should().Contain("not confirmed");
        await f.ViewModel.ExecuteQueueConfigurationCommandAsync(new(AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        f.QueuePreferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Protected_original_activated_setting_denies_without_write_or_downgrade()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        f.Voice.Microphones = [new("0", "Headset")];
        f.Voice.DefaultMicrophoneId = "0";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.SetVoiceConsentAsync(true);
        if (!f.ViewModel.IsVoiceEnabled) { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        f.CallState.SetState(Kora.Core.Communication.CallState.Active);
        await f.Dispatcher.LastInvocation;
        await f.ViewModel.ExecuteQueueConfigurationCommandAsync(new(AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots, "2"),
            Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        f.QueuePreferences.Writes.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Contain("denied");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    [InlineData("write")]
    public async Task Queue_configuration_does_not_replace_or_stop_complete_inflight_speech(string stage)
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = f.RunAsync("what power action is pending");
        await f.TextToSpeech.SpeakStarted.Task;
        var original = f.ViewModel.ResponseBody;
        var stops = f.TextToSpeech.StopCalls;
        if (stage is "write") { f.QueuePreferences.WriteFailure = new IOException(); }
        await f.ViewModel.ExecuteQueueConfigurationCommandAsync(new(AppearanceCommandOperation.Set, SessionQueueOption.ExecutionSlots, stage is "invalid" ? "3" : "2"),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        f.ViewModel.ResponseBody.Should().Be(original);
        f.TextToSpeech.StopCalls.Should().Be(stops);
        f.ViewModel.Transcript.Should().Contain(stage is "valid" ? "queue.execution-slots" : stage is "invalid" ? "Choose queue" : "not confirmed");
        f.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Activation_or_disposal_after_commit_cannot_publish_stale_success()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        var previous = f.ViewModel.ResponseBody;
        configuration.Changed += (_, _) =>
        {
            if (f.QueuePreferences.Value.PendingPerSession is not null)
            { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        };
        await f.RunAsync("set queue.pending-per-session to 1");
        f.QueuePreferences.Value.PendingPerSession.Should().Be(1);
        f.ViewModel.ResponseBody.Should().Be(previous);
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        f.Audit.BeforeWrite = item =>
        {
            if (item.ActionId is "configuration.queue-execution-slots"
                && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded) { f.ViewModel.Dispose(); }
        };
        await f.RunAsync("set queue.execution-slots to 2");
        configuration.Get().Available.Should().BeFalse();
    }

    [Fact]
    public async Task Captured_native_admission_retiring_after_activation_suppresses_success_without_relabelling()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindQueueConfigurationNativeLifetime(() => true);
        await f.ViewModel.RefreshQueueConfigurationCommand.ExecuteAsync();
        var live = true;
        typeof(Kora.Application.ViewModels.MainViewModel).GetField("queueNativeAdmission",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(f.ViewModel, (Func<bool>)(() => live));
        configuration.Changed += (_, _) => { if (f.QueuePreferences.Value.PendingPerSession == 1) { live = false; } };
        var previous = f.ViewModel.ResponseBody;
        f.ViewModel.SelectedQueuePending = 1;
        await f.ViewModel.SaveQueuePendingCommand.ExecuteAsync();
        f.QueuePreferences.Value.PendingPerSession.Should().Be(1);
        f.ViewModel.ResponseBody.Should().Be(previous);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Late_queue_failure_cannot_replace_an_exact_pending_approval()
    {
        var f = new Fixture(enableQueueConfiguration: true);
        await using var admission = f.OutputAdmission;
        await using var configuration = f.QueueConfiguration!;
        f.Probe.Status = new("local.inference", "Local model inference (Ollama)", Kora.Core.Dependencies.DependencyReadiness.Ready, "ready");
        f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("lock this workstation please");
        await f.ViewModel.ActiveReasoningTask!;
        var preview = f.ViewModel.ResponseBody;
        typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("PresentQueueConfigurationFailure",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(f.ViewModel, ["Queue settings not confirmed.", "owned late failure"]);
        f.ViewModel.ResponseBody.Should().Be(preview);
        f.ViewModel.Transcript.Should().Contain("owned late failure");
        f.ViewModel.Dispose();
    }
}
