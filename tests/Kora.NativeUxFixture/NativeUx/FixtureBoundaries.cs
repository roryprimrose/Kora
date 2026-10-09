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
    internal sealed class FixtureAccess : ISessionWorkspaceAccess, IEvidenceQueryAccess, ICapabilityHostAccess,
        ISessionController, IMicrophoneAccessService, IWindowsPrivacyObservationService, ICurrentUserNameProvider
    {
        private bool disposed;
        public bool Open { get; private set; } = true;
        public bool CanInspect => Open;
        public bool CanControl => Open;
        public bool IsCurrentHost => Open;
        public long ControlRevision { get; private set; }
        public WindowsPrivacySnapshot Current => new(Open ? WindowsSessionState.Unlocked : WindowsSessionState.Unknown,
            MicrophoneAccessState.Denied, ControlRevision, [], null, null);
        public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;
        public WindowsPrivacySnapshot Refresh() => Current;
        public bool IsCurrentSessionUnlocked() => Open;
        public string GetAddressName() => "Fixture";
        public MicrophoneAccessStatus GetStatus() => new(MicrophoneAccessState.Denied, "Fixture: microphone access is prohibited.");
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

    internal sealed class FixtureVoice : IActivatedVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized { add { } remove { } }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public bool IsListening => false;
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent => true;
        public void InvalidateCapture() => Generation++;
        public bool AcceptCaptureGeneration(long generation) => false;
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [];
        public MicrophoneDevice? GetDefaultMicrophone() => null;
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture cannot capture audio.");
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture cannot capture audio.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    internal sealed class FixtureCallState : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        public CallState CurrentState => CallState.Unavailable;
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