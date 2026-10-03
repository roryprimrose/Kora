using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;

using Kora.Core.Voice;

using NAudio;
using NAudio.Wave;

namespace Kora.Windows.Audio;

public sealed class WindowsVoiceRecognitionService : IVoiceRecognitionService
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const float MinimumConfidence = 0.62f;

    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private SpeechRecognitionEngine? recognizer;
    private WaveIn? waveIn;
    private BlockingAudioStream? audioStream;
    private bool disposed;

    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    public bool IsListening { get; private set; }

    public IReadOnlyList<MicrophoneDevice> GetMicrophones()
    {
        var devices = new List<MicrophoneDevice>(WaveIn.DeviceCount);

        for (var index = 0; index < WaveIn.DeviceCount; index++)
        {
            var capabilities = WaveIn.GetCapabilities(index);
            devices.Add(new MicrophoneDevice(index.ToString(CultureInfo.InvariantCulture), capabilities.ProductName));
        }

        return devices;
    }

    public async Task StartAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(phrases);

        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsListening)
            {
                return;
            }

            if (!int.TryParse(microphone.Id, CultureInfo.InvariantCulture, out var deviceNumber) ||
                deviceNumber < 0 ||
                deviceNumber >= WaveIn.DeviceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(microphone), "The selected microphone is no longer available.");
            }

            var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(info => string.Equals(
                    info.Culture.TwoLetterISOLanguageName,
                    "en",
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException("No English Windows speech recognizer is installed.");

            var grammarPhrases = phrases
                .SelectMany(phrase => new[] { phrase, $"kora {phrase}" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (grammarPhrases.Length == 0)
            {
                throw new ArgumentException("At least one recognition phrase is required.", nameof(phrases));
            }

            audioStream = new BlockingAudioStream();
            recognizer = new SpeechRecognitionEngine(recognizerInfo.Id);
            var grammarBuilder = new GrammarBuilder
            {
                Culture = recognizerInfo.Culture,
            };
            grammarBuilder.Append(new Choices(grammarPhrases));
            recognizer.LoadGrammar(new Grammar(grammarBuilder));
            recognizer.SpeechRecognized += OnSpeechRecognized;
            recognizer.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
            recognizer.SetInputToAudioStream(
                audioStream,
                new SpeechAudioFormatInfo(
                    EncodingFormat.Pcm,
                    SampleRate,
                    BitsPerSample,
                    Channels,
                    SampleRate * Channels * (BitsPerSample / 8),
                    Channels * (BitsPerSample / 8),
                    null));

            waveIn = new WaveIn
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = 100,
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += OnRecordingStopped;

            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            waveIn.StartRecording();
            IsListening = true;
        }
        catch (MmException exception)
        {
            DisposeRecognitionResources();
            throw new InvalidOperationException("Windows could not open the selected microphone.", exception);
        }
        catch
        {
            DisposeRecognitionResources();
            throw;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsListening && recognizer is null && waveIn is null)
            {
                return;
            }

            IsListening = false;
            waveIn?.StopRecording();
            audioStream?.Complete();
            recognizer?.RecognizeAsyncCancel();
            DisposeRecognitionResources();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopAsync();
        disposed = true;
        lifecycleLock.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (IsListening)
        {
            audioStream?.Add(eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded));
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs eventArgs)
    {
        if (eventArgs.Result.Confidence >= MinimumConfidence)
        {
            TranscriptRecognized?.Invoke(
                this,
                new VoiceTranscriptEventArgs(eventArgs.Result.Text, eventArgs.Result.Confidence));
        }
    }

    private void OnSpeechRecognitionRejected(object? sender, SpeechRecognitionRejectedEventArgs eventArgs)
    {
        RecognitionFailed?.Invoke(
            this,
            new VoiceRecognitionFailureEventArgs("I heard speech but could not match a supported command."));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            RecognitionFailed?.Invoke(
                this,
                new VoiceRecognitionFailureEventArgs($"Microphone capture stopped: {eventArgs.Exception.Message}"));
        }
    }

    private void DisposeRecognitionResources()
    {
        if (waveIn is not null)
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.Dispose();
            waveIn = null;
        }

        if (recognizer is not null)
        {
            recognizer.SpeechRecognized -= OnSpeechRecognized;
            recognizer.SpeechRecognitionRejected -= OnSpeechRecognitionRejected;
            recognizer.Dispose();
            recognizer = null;
        }

        audioStream?.Dispose();
        audioStream = null;
    }
}