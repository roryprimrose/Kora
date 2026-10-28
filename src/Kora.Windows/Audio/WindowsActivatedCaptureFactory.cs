using System.Speech.AudioFormat;
using System.Speech.Recognition;

using Kora.Core.Voice;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedCaptureFactory : IActivatedCaptureFactory
{
    public Task<IActivatedCapture> OpenAsync(
        MicrophoneDevice microphone,
        IReadOnlyList<string> phrases,
        BlockingAudioStream stream,
        Func<bool> canOpen) =>
        OpenAsync(microphone, phrases, stream, canOpen, VoiceCaptureLimits.Default.EmptySpeechDeadline);

    public async Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone,
        IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow)
    {
        MMDevice? endpoint = null;
        SpeechRecognitionEngine? recognizer = null;
        WasapiRecorder? recorder = null;
        try
        {
            WindowsCapturePrivacyGate.RequireEligible(microphone);
            recognizer = CreateRecognizer(phrases, stream, speechStartWindow);
            (recorder, endpoint) = await OpenMicrophoneAsync(microphone, canOpen);
            return new WindowsActivatedCapture(recognizer, recorder, endpoint, microphone, canOpen);
        }
        catch
        {
            try
            {
                if (recorder is not null) { await recorder.DisposeAsync(); }
            }
            finally
            {
                recognizer?.Dispose();
                endpoint?.Dispose();
            }
            throw;
        }
    }

    internal static IActivatedCapture OpenCommand(
        MicrophoneDevice microphone, IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen)
    {
        if (!canOpen()) { throw new OperationCanceledException("Command audio admission was retired."); }
        var recognizer = CreateRecognizer(phrases, stream, VoiceCaptureLimits.Default.EmptySpeechDeadline);
        return new WindowsActivatedCapture(recognizer, null, null, microphone, canOpen);
    }

    private static SpeechRecognitionEngine CreateRecognizer(
        IReadOnlyList<string> phrases, BlockingAudioStream stream, TimeSpan speechStartWindow)
    {
        var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(candidate =>
            string.Equals(candidate.Culture.TwoLetterISOLanguageName, "en", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No English Windows speech recognizer is installed.");
        var recognizer = new SpeechRecognitionEngine(info.Id)
        {
            InitialSilenceTimeout = speechStartWindow,
            EndSilenceTimeout = TimeSpan.FromSeconds(1),
            EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1),
        };
        try
        {
            if (phrases.Count != 0)
            {
                var grammar = new GrammarBuilder { Culture = info.Culture };
                grammar.Append(new Choices(phrases.ToArray()));
                recognizer.LoadGrammar(new Grammar(grammar));
            }
            // General transcription is admitted only after explicit or exact wake activation.
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToAudioStream(stream,
                new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
            return recognizer;
        }
        catch
        {
            recognizer.Dispose();
            throw;
        }
    }

    internal static async Task<(WasapiRecorder Recorder, MMDevice? Endpoint)> OpenMicrophoneAsync(
        MicrophoneDevice microphone, Func<bool> canOpen)
    {
        MMDevice? endpoint = null;
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

            return (recorder, endpoint);
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
                endpoint?.Dispose();
            }
            throw;
        }
    }
}
