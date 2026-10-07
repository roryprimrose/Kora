using AwesomeAssertions;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static async Task LoadSavedOutputAsync(Fixture fixture, string outputId)
    {
        fixture.AudioPreferences.OutputDeviceId = outputId;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync();
    }

    [Theory]
    [InlineData("list audio output settings")]
    [InlineData("get audio.output-device")]
    [InlineData("status audio.output-device")]
    [InlineData("set audio.output-device to system-default")]
    [InlineData("reset audio.output-device")]
    [InlineData("Kora, SET audio.output-device to arbitrary endpoint")]
    public async Task Reserved_output_configuration_requests_fail_locally_without_admission(string command)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var selection = fixture.ViewModel.SelectedOutputDevice;
        var captureStarts = fixture.Voice.StartCalls;
        var consent = fixture.ViewModel.HasVoiceConsent;
        var auditCount = fixture.Audit.Events.Count;
        await fixture.RunAsync(command);
        fixture.ViewModel.State.Should().Be(AssistantState.Failure);
        fixture.ViewModel.ResponseBody.Should().Contain("admitted session/generation");
        fixture.ViewModel.SelectedOutputDevice.Should().BeSameAs(selection);
        fixture.ViewModel.CanChangeAudioOutputDevice.Should().BeFalse();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.AudioPreferences.ClearedOutputDeviceCount.Should().Be(0);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(captureStarts);
        fixture.ViewModel.HasVoiceConsent.Should().Be(consent);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Count.Should().Be(auditCount);
    }

    [Fact]
    public async Task Original_voice_and_protected_call_cannot_gain_output_authority()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var selected = fixture.ViewModel.SelectedOutputDevice;
        await fixture.RaiseActivatedTranscriptAsync("Kora, set audio.output-device to 0", 1);
        fixture.ViewModel.ResponseBody.Should().Contain("unavailable");
        fixture.CallState.SetState(CallState.Active);
        await fixture.Dispatcher.LastInvocation;
        await fixture.RunAsync("reset audio.output-device");
        fixture.ViewModel.SelectedOutputDevice.Should().BeSameAs(selected);
        fixture.AudioPreferences.ClearedOutputDeviceCount.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Native_reentrant_foreign_and_disposed_selections_do_not_mutate_saved_or_live_route()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var selected = fixture.ViewModel.SelectedOutputDevice;
        fixture.ViewModel.SelectedOutputDevice = selected;
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SelectedOutputDevice), StringComparison.Ordinal))
            {
                fixture.ViewModel.SelectedOutputDevice = new AudioOutputDevice("foreign", "Same friendly name");
            }
        };
        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        fixture.ViewModel.SelectedOutputDevice.Should().BeSameAs(selected);
        fixture.ViewModel.Dispose();
        fixture.ViewModel.SelectedOutputDevice = null;
        fixture.ViewModel.SelectedOutputDevice.Should().BeSameAs(selected);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.AudioPreferences.ClearedOutputDeviceCount.Should().Be(0);
    }

    [Fact]
    public async Task Unavailable_output_request_preserves_exact_pending_approval_and_never_answers_or_grants_it()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus(
            "local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        fixture.Reasoner.Action = BuiltInAction.LockMachine;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("lock this workstation please");
        await fixture.ViewModel.ActiveReasoningTask!;
        var title = fixture.ViewModel.ResponseTitle;
        var preview = fixture.ViewModel.ResponseBody;
        var requests = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync("set audio.output-device to system-default");
        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        fixture.ViewModel.IsModelActionApprovalPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(preview);
        fixture.ViewModel.Transcript.Should().Contain("Audio output preference changes are unavailable");
        fixture.Reasoner.Requests.Count.Should().Be(requests);
        fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
        fixture.ApprovalPreferences.Preferences.AlwaysAllowedActions.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public async Task Rejected_native_output_change_preserves_in_flight_response_and_never_restarts_playback()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.TextToSpeech.SpeakGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var speaking = fixture.RunAsync("what power action is pending");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var spoken = fixture.TextToSpeech.SpokenText;
        var stops = fixture.TextToSpeech.StopCalls;
        fixture.ViewModel.SelectedOutputDevice = fixture.TextToSpeech.OutputDevices[0];
        await fixture.RunAsync("reset audio.output-device");
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.TextToSpeech.SpokenText.Should().Be(spoken);
        fixture.TextToSpeech.StopCalls.Should().Be(stops);
        fixture.ViewModel.SelectedOutputDevice.Should().Be(SystemAudioDevices.Output);
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await speaking;
    }
}
