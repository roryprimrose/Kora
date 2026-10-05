using System.Speech.AudioFormat;
using System.Speech.Recognition;

using Kora.Core.Voice;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedCaptureFactory : IActivatedCaptureFactory
{
    public async Task<IActivatedCapture> OpenAsync(
        MicrophoneDevice microphone,
        IReadOnlyList<string> phrases,
        BlockingAudioStream stream,
        Func<bool> canOpen)
    {
        MMDevice? endpoint = null;
        SpeechRecognitionEngine? recognizer = null;
        WasapiRecorder? recorder = null;
        try
        {
            WindowsCapturePrivacyGate.RequireEligible(microphone);
            if (!microphone.IsSystemDefault)
            {
                using var enumerator = new MMDeviceEnumerator();
                endpoint = enumerator.GetDevice(microphone.Id);
                if (endpoint.State != DeviceState.Active)
                {
                    throw new InvalidOperationException("The selected microphone is no longer active.");
                }
            }

            var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(candidate =>
                string.Equals(candidate.Culture.TwoLetterISOLanguageName, "en", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("No English Windows speech recognizer is installed.");
            recognizer = new SpeechRecognitionEngine(info.Id)
            {
                InitialSilenceTimeout = TimeSpan.FromSeconds(5),
                EndSilenceTimeout = TimeSpan.FromSeconds(1),
                EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1),
            };
            if (phrases.Count != 0)
            {
                var grammar = new GrammarBuilder { Culture = info.Culture };
                grammar.Append(new Choices(phrases.ToArray()));
                recognizer.LoadGrammar(new Grammar(grammar));
            }

            // Dictation is admitted only after explicit activation, never as a wake detector.
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToAudioStream(stream,
                new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
            var builder = new WasapiRecorderBuilder()
                .WithFormat(new WaveFormat(16000, 16, 1))
                .WithBufferLength(100);
            if (microphone.IsSystemDefault)
            {
                builder.WithDefaultDeviceStreamRouting();
            }
            else
            {
                builder.WithDevice(endpoint!);
            }

            if (!canOpen())
            {
                throw new OperationCanceledException("Privacy prerequisites changed before microphone open.");
            }

            WindowsCapturePrivacyGate.RequireEligible(microphone);
            recorder = await builder.BuildAsync();
            WindowsCapturePrivacyGate.RequireEligible(microphone);
            if (!canOpen())
            {
                throw new OperationCanceledException("Capture was invalidated during the native microphone open.");
            }

            return new WindowsActivatedCapture(recognizer, recorder, endpoint, microphone, canOpen);
        }
        catch
        {
            try
            {
                if (recorder is not null)
                {
                    await recorder.DisposeAsync();
                }
            }
            finally
            {
                recognizer?.Dispose();
                endpoint?.Dispose();
            }
            throw;
        }
    }
}
