using System.Speech.AudioFormat;
using System.Speech.Recognition;

using Kora.Core.Voice;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedNativeFactory : IWindowsActivatedNativeFactory
{
    // Deterministic implementation evidence does not qualify System.Speech reset/content release.
    public bool IsKeepLoadedQualified => false;

    public void RequireEligible(MicrophoneDevice microphone) => WindowsCapturePrivacyGate.RequireEligible(microphone);

    public IWindowsActivatedRecognizer CreateRecognizer(IReadOnlyList<string> phrases, TimeSpan speechStartWindow, TimeSpan trailingSilence) =>
        new Recognizer(phrases, speechStartWindow, trailingSilence);

    public async Task<IWindowsActivatedMicrophone> OpenMicrophoneAsync(MicrophoneDevice microphone, Func<bool> canOpen)
    {
        var (recorder, endpoint) = await WindowsActivatedCaptureFactory.OpenMicrophoneAsync(microphone, canOpen).ConfigureAwait(false);
        return new Microphone(recorder, endpoint);
    }

    private sealed class Recognizer : IWindowsActivatedRecognizer
    {
        private readonly Lock callbacks = new();
        private readonly SpeechRecognitionEngine engine;
        private Binding? binding;

        public Recognizer(IReadOnlyList<string> phrases, TimeSpan speechStartWindow, TimeSpan trailingSilence)
        {
            var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(candidate =>
                string.Equals(candidate.Culture.TwoLetterISOLanguageName, "en", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("No English Windows speech recognizer is installed.");
            engine = new SpeechRecognitionEngine(info.Id)
            {
                InitialSilenceTimeout = speechStartWindow,
                EndSilenceTimeout = trailingSilence,
                EndSilenceTimeoutAmbiguous = trailingSilence,
            };
            try
            {
                if (phrases.Count != 0)
                {
                    var grammar = new GrammarBuilder { Culture = info.Culture };
                    grammar.Append(new Choices(phrases.ToArray()));
                    engine.LoadGrammar(new Grammar(grammar));
                }
                engine.LoadGrammar(new DictationGrammar());
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }

        public event EventHandler? SpeechDetected;
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<ActivatedHypothesisEventArgs>? Hypothesized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
        public event EventHandler<WindowsRecognitionCompletedEventArgs>? Completed;

        private void ReportDetected() => SpeechDetected?.Invoke(this, EventArgs.Empty);
        private void ReportRecognized(RecognitionResult result) => TranscriptRecognized?.Invoke(this, new(result.Text, result.Confidence));
        private void ReportHypothesis(RecognitionResult result, long sequence) => Hypothesized?.Invoke(this, new(result.Text, sequence));
        private void ReportRejected() => RecognitionFailed?.Invoke(this, new("Speech did not match a supported command."));
        private void ReportCompleted(Exception? error) => Completed?.Invoke(this, new(error));

        public void BindInput(BlockingAudioStream stream)
        {
            lock (callbacks)
            {
                if (binding is not null) { throw new InvalidOperationException("The recognizer still owns an input binding."); }
                binding = new Binding(this);
                binding.Subscribe(engine);
                engine.SetInputToAudioStream(stream,
                    new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
            }
        }

        public void Start() => engine.RecognizeAsync(RecognizeMode.Single);
        public void Finish(bool cancel)
        {
            if (cancel) { engine.RecognizeAsyncCancel(); }
            else { engine.RecognizeAsyncStop(); }
        }

        public Task ResetAsync()
        {
            RetireBinding();
            return Task.Run(engine.SetInputToNull, CancellationToken.None);
        }

        private void RetireBinding()
        {
            lock (callbacks)
            {
                var previous = binding;
                binding = null;
                previous?.Unsubscribe(engine);
            }
        }

        public async ValueTask DisposeAsync()
        {
            RetireBinding();
            await Task.Run(engine.Dispose, CancellationToken.None).ConfigureAwait(false);
        }

        private sealed class Binding(Recognizer owner)
        {
            private long sequence;

            public void Subscribe(SpeechRecognitionEngine engine)
            {
                engine.SpeechDetected += Detected;
                engine.SpeechRecognized += Recognized;
                engine.SpeechHypothesized += Hypothesized;
                engine.SpeechRecognitionRejected += Rejected;
                engine.RecognizeCompleted += Completed;
            }

            public void Unsubscribe(SpeechRecognitionEngine engine)
            {
                engine.SpeechDetected -= Detected;
                engine.SpeechRecognized -= Recognized;
                engine.SpeechHypothesized -= Hypothesized;
                engine.SpeechRecognitionRejected -= Rejected;
                engine.RecognizeCompleted -= Completed;
            }

            private void Dispatch(Action callback)
            {
                lock (owner.callbacks)
                {
                    if (ReferenceEquals(owner.binding, this)) { callback(); }
                }
            }

            private void Detected(object? sender, SpeechDetectedEventArgs args) =>
                Dispatch(owner.ReportDetected);
            private void Recognized(object? sender, SpeechRecognizedEventArgs args) =>
                Dispatch(() => owner.ReportRecognized(args.Result));
            private void Hypothesized(object? sender, SpeechHypothesizedEventArgs args) =>
                Dispatch(() => owner.ReportHypothesis(args.Result, Interlocked.Increment(ref sequence)));
            private void Rejected(object? sender, SpeechRecognitionRejectedEventArgs args) =>
                Dispatch(owner.ReportRejected);
            private void Completed(object? sender, RecognizeCompletedEventArgs args) =>
                Dispatch(() => owner.ReportCompleted(args.Error));
        }
    }

    private sealed class Microphone : IWindowsActivatedMicrophone
    {
        private readonly WasapiRecorder recorder;
        private readonly MMDevice? endpoint;

        public Microphone(WasapiRecorder recorder, MMDevice? endpoint)
        {
            this.recorder = recorder;
            this.endpoint = endpoint;
            recorder.DataAvailable += Audio;
            recorder.RecordingStopped += Stopped;
        }

        public event WindowsCaptureAudioAvailable? DataAvailable;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecordingFailed;

        public void Start() => recorder.StartRecording();

        public async ValueTask DisposeAsync()
        {
            recorder.DataAvailable -= Audio;
            recorder.RecordingStopped -= Stopped;
            try
            {
                try { recorder.StopRecording(); }
                finally { await recorder.DisposeAsync().ConfigureAwait(false); }
            }
            finally { endpoint?.Dispose(); }
        }

        private void Audio(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long position, long timestamp) =>
            DataAvailable?.Invoke(buffer);
        private void Stopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is not null) { RecordingFailed?.Invoke(this, new("Microphone capture failed.")); }
        }
    }
}
