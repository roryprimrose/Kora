using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Communication;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Core.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class QuestionVoiceAdapter(FakeVoiceRecognitionService voice) : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
    {
        private readonly SpeechInputTestRuntime runtime = new(voice);
        public SpeechInputGeneration ProfileGeneration => runtime.ProfileGeneration;
        public bool IsProfileAvailable => runtime.IsProfileAvailable;
        public void InitializeProfile(SpeechInputSelection? selection) => runtime.InitializeProfile(selection);
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken) => runtime.RetireProfileAsync(cancellationToken);
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized
        { add => voice.TranscriptRecognized += value; remove => voice.TranscriptRecognized -= value; }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed
        { add => voice.RecognitionFailed += value; remove => voice.RecognitionFailed -= value; }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged
        { add => voice.CaptureStateChanged += value; remove => voice.CaptureStateChanged -= value; }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted
        { add => voice.RecognitionCompleted += value; remove => voice.RecognitionCompleted -= value; }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted;
        public bool IsListening => voice.IsListening;
        public long Generation => voice.Generation;
        public long CaptureGeneration => voice.Generation;
        public bool IsAmbientListeningAvailable => false;
        public bool IsLiteralDictationAvailable => voice.IsLiteralDictationAvailable;
        public bool IsCaptureQuiescent => voice.IsCaptureQuiescent;
        public bool AcceptCaptureGeneration(long generation) => voice.AcceptCaptureGeneration(generation);
        public async Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            Func<bool> isAdmitted, CancellationToken cancellationToken)
        {
            isAdmitted().Should().BeTrue();
            await voice.StartAsync(microphone, phrases, cancellationToken: cancellationToken);
        }
        public void Speech() => ReplySpeechStarted?.Invoke(this, new(Generation, true));
        public void InvalidateCapture() => voice.InvalidateCapture();
        public Task StopAsync(CancellationToken cancellationToken = default) => voice.StopAsync(cancellationToken);
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => voice.StartAsync(microphone, phrases, assistantName, cancellationToken);
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => voice.BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => voice.EndPushToTalkAsync(cancellationToken);
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => voice.EndCaptureAsync(cancellationToken);
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => voice.GetMicrophones();
        public MicrophoneDevice? GetDefaultMicrophone() => voice.GetDefaultMicrophone();
        public ValueTask DisposeAsync() => voice.DisposeAsync();
    }

    private static async Task<Fixture> ReplyHostAsync()
    {
        var f = new Fixture(enableQuestionReplies: true);
        f.VoiceConsent.Consent = true;
        f.Voice.Microphones = [new("mic", "Synthetic headset")];
        f.Voice.DefaultMicrophoneId = "mic";
        f.ViewModel.BindVoiceOwnershipGate(static () => true);
        await f.ViewModel.InitializeAsync();
        return f;
    }

    [Fact]
    public async Task InstructionSaveVoiceEligibilityPinsReadinessWithoutOpeningCapture()
    {
        var unavailable = CreateVoicePrivacyFixture();
        unavailable.ViewModel.CaptureInstructionSaveVoiceEligibility().Should().BeNull();
        unavailable.ViewModel.Dispose();
        var f = await ReplyHostAsync();
        var current = f.ViewModel.CaptureInstructionSaveVoiceEligibility();
        current.Should().NotBeNull();
        current!().Should().BeTrue();
        f.Voice.IsListening.Should().BeFalse();
        f.ViewModel.SelectedMicrophone = new("other", "Unavailable");
        current().Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Native_question_reply_binding_requires_supported_endpointing_and_a_unique_application_owner()
    {
        var legacy = CreateVoicePrivacyFixture();
        using var host = new InteractionFixture();
        legacy.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance).Should().BeNull();
        var f = await ReplyHostAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        var duplicate = () => f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance);
        duplicate.Should().Throw<InvalidOperationException>();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Exact_reply_never_reaches_command_router_model_network_or_composer()
    {
        var f = await ReplyHostAsync();
        var editorCalls = 0;
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        f.ViewModel.BindCustomizationVoice(_ => { editorCalls++; return Task.CompletedTask; });
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.ReplyVoice!.Speech();
        f.Voice.RaiseTranscript("First", 1);
        await replies.Quiescence;
        host.Store.Snapshot.Questions.Single().Draft!.Choices.Should().Equal("a");
        f.Session.LockCalls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        f.ViewModel.IsVoiceEnabled.Should().BeTrue();
        f.Voice.IsListening.Should().BeFalse();
        editorCalls.Should().Be(0, "dedicated question capture owns input even with an eligible editor");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("lock")]
    [InlineData("disconnect")]
    [InlineData("unknown")]
    [InlineData("consent")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("device")]
    [InlineData("default")]
    [InlineData("close")]
    public async Task Main_runtime_boundaries_synchronously_retire_question_capture_and_preserve_question(string boundary)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.IsListening.Should().BeTrue();
        var generation = f.Voice.Generation;
        switch (boundary)
        {
            case "disable": await f.ViewModel.DisableListeningFromTrayAsync(); break;
            case "lock": PublishVoiceSession(f, WindowsSessionState.Locked); break;
            case "disconnect": PublishVoiceSession(f, WindowsSessionState.Disconnected); break;
            case "unknown": PublishVoiceSession(f, WindowsSessionState.Unknown); break;
            case "consent": await f.ViewModel.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); replies.Revalidate(); break;
            case "call": f.CallState.SetState(CallState.Unknown); break;
            case "device": f.ViewModel.SelectedMicrophone = new("other", "Unavailable"); break;
            case "default":
                var previous = f.PrivacyObservation.Current;
                var next = previous with { TopologyRevision = 1, DefaultMicrophoneId = "other" };
                f.PrivacyObservation.Current = next;
                f.PrivacyObservation.Publish(new(previous, next, WindowsPrivacyChangeReason.DefaultMicrophone));
                break;
            case "close": f.ViewModel.Dispose(); break;
        }
        f.Voice.IsListening.Should().BeFalse();
        f.Voice.RaiseTranscriptForGeneration(generation);
        await replies.Quiescence;
        host.Store.Snapshot.Questions.Single().Status.Should().Be(Kora.Core.Interaction.QuestionStatus.Pending);
        f.Session.LockCalls.Should().Be(0);
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.StartCalls.Should().Be(1);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Post_playback_open_uses_exact_host_text_and_never_captures_while_speaking()
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = replies.OpenAsync(question, static () => true, _ => { }, true, TestContext.Current.CancellationToken);
        f.TextToSpeech.SpokenText.Should().Be(question.Spec.Text);
        f.TextToSpeech.IsSpeaking.Should().BeTrue();
        f.Voice.StartCalls.Should().Be(0);
        f.TextToSpeech.SpeakGate.SetResult();
        await opening;
        f.Voice.StartCalls.Should().Be(1);
        replies.Retire("Cancel");
        await replies.Quiescence;
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("catalogue")]
    [InlineData("catalogue-revision")]
    [InlineData("consent")]
    [InlineData("permission")]
    [InlineData("busy")]
    [InlineData("presented-output")]
    [InlineData("provider-output")]
    public async Task Explicit_reply_revalidates_every_live_readiness_surface_before_capture(string denial)
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        Task? speaking = null;
        switch (denial)
        {
            case "session": f.Session.IsUnlocked = false; break;
            case "catalogue": Field(f, "microphoneCatalogCurrent", false); break;
            case "catalogue-revision": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 1 }; break;
            case "consent": Field(f, "voiceConsent", false); break;
            case "permission":
                typeof(Kora.Application.ViewModels.MainViewModel).GetProperty(nameof(f.ViewModel.MicrophoneAccessStatus))!
                    .SetValue(f.ViewModel, new MicrophoneAccessStatus(MicrophoneAccessState.Denied, "Synthetic"));
                break;
            case "busy": Field(f, "isBusy", true); break;
            case "presented-output": Field(f, "isSpeaking", true); break;
            case "provider-output":
                f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                speaking = f.TextToSpeech.SpeakAsync("Synthetic", f.TextToSpeech.Voices[0], f.TextToSpeech.OutputDevices[0],
                    TestContext.Current.CancellationToken);
                break;
        }
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.StartCalls.Should().Be(0);
        if (speaking is not null) { f.TextToSpeech.SpeakGate!.SetResult(); await speaking; }
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task A_model_question_is_not_the_native_foreground_voice_target()
    {
        var f = await ReplyHostAsync();
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "Synthetic");
        f.Reasoner.Question = new("Which?", ["One", "Two"]);
        await f.ViewModel.RefreshCommand.ExecuteAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await f.RunAsync("help with this computer");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("output")]
    [InlineData("admission")]
    [InlineData("policy")]
    [InlineData("native-speaking")]
    [InlineData("presented-speaking")]
    public async Task Exact_playback_seam_fails_closed_before_synthesis_for_missing_or_competing_state(string denial)
    {
        var f = await ReplyHostAsync();
        var eligible = true;
        Task? speaking = null;
        switch (denial)
        {
            case "voice": Field(f, "activeSpeechVoice", null); break;
            case "output": Field(f, "selectedOutputDevice", null); break;
            case "admission": eligible = false; break;
            case "policy": f.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; break;
            case "native-speaking":
                f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                speaking = f.TextToSpeech.SpeakAsync("Synthetic", f.TextToSpeech.Voices[0], f.TextToSpeech.OutputDevices[0],
                    TestContext.Current.CancellationToken);
                break;
            case "presented-speaking": Field(f, "isSpeaking", true); break;
        }
        var speak = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("SpeakExactQuestionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, Func<bool>, CancellationToken, Task>>(f.ViewModel);
        var attempt = () => speak("Exact synthetic question", () => eligible, TestContext.Current.CancellationToken);
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        if (speaking is not null) { f.TextToSpeech.SpeakGate!.SetResult(); await speaking; }
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("not-started")]
    [InlineData("lost-admission")]
    [InlineData("unfinished")]
    public async Task Only_successful_completed_exact_playback_can_qualify_a_reply(string failure)
    {
        var f = await ReplyHostAsync();
        var calls = 0;
        bool Eligible()
        {
            calls++;
            return !string.Equals(failure, "not-started", StringComparison.Ordinal) || calls == 1;
        }
        var eligible = true;
        f.TextToSpeech.BeforeSpeak = () =>
        {
            if (string.Equals(failure, "lost-admission", StringComparison.Ordinal)) { eligible = false; }
        };
        if (string.Equals(failure, "unfinished", StringComparison.Ordinal)) { f.TextToSpeech.ReturnWhileSpeaking = true; }
        var speak = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("SpeakExactQuestionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, Func<bool>, CancellationToken, Task>>(f.ViewModel);
        var attempt = () => speak("Exact synthetic question", () => Eligible() && eligible, TestContext.Current.CancellationToken);
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task Host_handoff_waits_for_reply_quiescence_without_replaying_capture()
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, static () => true, _ => { }, false, TestContext.Current.CancellationToken);
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        replies.IsActive.Should().BeFalse();
        f.ViewModel.Dispose();
    }

    private static void Field(Fixture f, string name, object? value) =>
        typeof(Kora.Application.ViewModels.MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.ViewModel, value);
}
