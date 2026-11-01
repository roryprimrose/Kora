using Kora.Core.Communication;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Platform;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Core.Voice;

namespace Kora.NativeUxFixture;

internal static class FixtureBoundaries
{
    internal sealed class FixtureAccess(bool syntheticVoice = false) : ISessionWorkspaceAccess, IEvidenceQueryAccess, ICapabilityHostAccess,
        ISessionController, IMicrophoneAccessService, IWindowsPrivacyObservationService, ICurrentUserNameProvider
    {
        private bool disposed;
        public bool Open { get; private set; } = true;
        public bool CanInspect => Open;
        public bool CanControl => Open;
        public bool IsCurrentHost => Open;
        public long ControlRevision { get; private set; }
        public WindowsPrivacySnapshot Current => new(Open ? WindowsSessionState.Unlocked : WindowsSessionState.Unknown,
            syntheticVoice ? MicrophoneAccessState.Allowed : MicrophoneAccessState.Denied, ControlRevision,
            syntheticVoice ? [FixtureVoice.SyntheticMicrophone.Id] : [],
            syntheticVoice ? FixtureVoice.SyntheticMicrophone.Id : null, null);
        public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;
        public WindowsPrivacySnapshot Refresh() => Current;
        public bool IsCurrentSessionUnlocked() => Open;
        public string GetAddressName() => "Fixture";
        public MicrophoneAccessStatus GetStatus() => syntheticVoice
            ? new(MicrophoneAccessState.Allowed, "Fixture: synthetic capture only; no device access.")
            : new(MicrophoneAccessState.Denied, "Fixture: microphone access is prohibited.");
        public bool LockCurrentSession() => throw new InvalidOperationException("Fixture cannot lock Windows.");

        internal void SetOpen(bool open)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var previous = Current;
            Open = open;
            ControlRevision++;
            Changed?.Invoke(this, new(previous, Current));
        }

        public void Dispose()
        {
            if (disposed) { return; }
            SetOpen(false);
            disposed = true;
            Changed = null;
        }
    }

    internal sealed class FixtureVoice(bool syntheticCapture = false) : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
    {
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(0, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; } = true;
        public void InitializeProfile(SpeechInputSelection? selection)
        {
            IsProfileAvailable = selection is not null && SpeechInputProfileCatalogue.UnavailableReason(selection) is null;
            if (selection is not null) { ProfileGeneration = new(0, selection); }
        }
        public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
        {
            await StopAsync(cancellationToken);
            IsProfileAvailable = false;
            return new ProfileSwitch(this);
        }
        private sealed class ProfileSwitch(FixtureVoice owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation)
            {
                owner.ProfileGeneration = generation;
                owner.IsProfileAvailable = SpeechInputProfileCatalogue.UnavailableReason(generation.Selection) is null;
            }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        internal static MicrophoneDevice SyntheticMicrophone { get; } = new("fixture-synthetic-microphone", "Synthetic fixture microphone");
        internal bool SyntheticCapture => syntheticCapture;
        internal int StartCalls { get; private set; }
        internal int EndCalls { get; private set; }
        internal int ReplyCalls { get; private set; }
        internal TaskCompletionSource ReplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? PendingLiteralOpen { get; set; }
        internal TaskCompletionSource? PendingLiteralStop { get; set; }
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted { add { } remove { } }
        public bool IsListening { get; private set; }
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public bool IsAmbientListeningAvailable => false;
        public bool IsLiteralDictationAvailable => syntheticCapture;
        public bool IsCaptureQuiescent => !IsListening;
        public void InvalidateCapture()
        {
            Generation++; IsListening = false;
            CaptureStateChanged?.Invoke(this, new(Generation, false));
        }
        public bool AcceptCaptureGeneration(long generation) => syntheticCapture && generation == Generation;
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => syntheticCapture ? [SyntheticMicrophone] : [];
        public MicrophoneDevice? GetDefaultMicrophone() => syntheticCapture ? SyntheticMicrophone : null;
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture cannot capture audio.");
        public async Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default)
        {
            if (!syntheticCapture) { throw new InvalidOperationException("Fixture cannot capture audio."); }
            cancellationToken.ThrowIfCancellationRequested();
            if (microphone != SyntheticMicrophone && microphone != SystemAudioDevices.Microphone)
            { throw new InvalidOperationException("Only the synthetic fixture endpoint or its system-default alias is admitted."); }
            Generation++; StartCalls++; IsListening = true; Started.TrySetResult();
            if (PendingLiteralOpen is { } pending) { await pending.Task; }
        }
        public Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            Func<bool> isAdmitted, CancellationToken cancellationToken)
        {
            if (!isAdmitted()) { throw new InvalidOperationException("Exact synthetic reply admission retired."); }
            var opened = BeginPushToTalkAsync(microphone, phrases, cancellationToken: cancellationToken);
            ReplyCalls++; ReplyStarted.TrySetResult();
            return opened;
        }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (PendingLiteralStop is { } pending) { await pending.Task; }
            IsListening = false;
        }
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => EndPushToTalkAsync(cancellationToken);
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default)
        {
            if (IsListening) { EndCalls++; IsListening = false; }
            return Task.CompletedTask;
        }
        internal void PublishTranscript(string text)
        {
            if (!syntheticCapture) { throw new InvalidOperationException("Synthetic capture was not enabled."); }
            TranscriptRecognized?.Invoke(this, new(text, 1, Generation));
        }
        internal Action QueueLiteralTranscript(string text)
        {
            var handler = TranscriptRecognized;
            var generation = Generation;
            return () => handler?.Invoke(this, new(text, 1, generation));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FixtureSpeech : ITextToSpeechService
    {
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public IReadOnlyList<SpeechProvider> GetProviders() => [];
        public IReadOnlyList<SpeechVoice> GetVoices() => [];
        public SpeechVoice? GetDefaultVoice() => null;
        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => [];
        public AudioOutputDevice? GetDefaultOutputDevice() => null;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture cannot produce speech.");
        public Task InstallProviderAsync(string providerId, IProgress<SpeechProviderInstallProgress> progress,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture cannot install providers.");
        public Task RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fixture cannot remove providers.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FixtureExecution : ILocalModelSetup, ILocalModelReasoner, IPowerShellSetup, IApplicationProcessController
    {
        public string TaskId => "fixture.setup.denied";
        public string TaskName => "Setup is unavailable in the native UX fixture";
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DependencyStatus(TaskId, TaskName, DependencyReadiness.Blocked, "Synthetic fixture; no capability qualification."));
        public Task InstallAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Fixture cannot install dependencies.");
        public Task InstallAsync(IProgress<LocalModelSetupProgress> progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fixture cannot install models.");
        public Task<LocalModelResponse> ReasonAsync(string request, LocalModelContext context, LocalModelArtifact? artifact,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Fixture cannot invoke a model.");
        public void OpenWindowsMicrophonePrivacySettings() => throw new InvalidOperationException("Fixture cannot open Windows settings.");
        public void RestartCurrentApplication() => throw new InvalidOperationException("Fixture cannot restart applications.");
    }

    internal sealed class FixtureCallState(CallState state = CallState.Unavailable) : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        public CallState CurrentState => state;
    }

    internal sealed class FixtureClipboardReader : IPlainTextClipboardReader
    {
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Shared clipboard access is prohibited in the native UX fixture.");
    }

    internal sealed class FixtureEvidenceGaps : IEvidenceGapReporter
    {
        public void Report(EvidenceGap gap) => throw new InvalidDataException("Native UX fixture evidence capture failed: " + gap);
    }
}