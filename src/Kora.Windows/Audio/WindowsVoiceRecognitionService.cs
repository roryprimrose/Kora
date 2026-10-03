using System.Runtime.InteropServices;
using System.Speech.AudioFormat;
using System.Speech.Recognition;

using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Audio;

public sealed class WindowsVoiceRecognitionService(
    ILogger<WindowsVoiceRecognitionService> logger) : IVoiceRecognitionService
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const float MinimumConfidence = 0.62f;

    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private SpeechRecognitionEngine? recognizer;
    private WasapiRecorder? recorder;
    private MMDevice? activeInputDevice;
    private BlockingAudioStream? audioStream;
    private TaskCompletionSource? recognitionCompletion;
    private bool disposed;

    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    public bool IsListening { get; private set; }

    public IReadOnlyList<MicrophoneDevice> GetMicrophones()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var devices = new List<MicrophoneDevice>(endpoints.Count);
            foreach (var endpoint in endpoints)
            {
                devices.Add(new MicrophoneDevice(endpoint.ID, endpoint.FriendlyName));
                endpoint.Dispose();
            }

            var result = devices
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            WindowsLog.DevicesEnumerated(logger, result.Length, "microphone");
            return result;
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Enumerating Windows microphone input");
            throw new InvalidOperationException(
                "Windows could not enumerate microphone input devices.",
                exception);
        }
    }

    public MicrophoneDevice? GetDefaultMicrophone()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return endpoint.State == DeviceState.Active
                ? new MicrophoneDevice(endpoint.ID, endpoint.FriendlyName)
                : null;
        }
        catch (COMException)
        {
            WindowsLog.Warning(logger, "No active Windows multimedia input endpoint is available");
            return null;
        }
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
        var started = false;
        var recognitionStarted = false;
        try
        {
            if (IsListening)
            {
                WindowsLog.Debug(logger, "Ignoring a duplicate voice activation start request");
                return;
            }

            if (microphone.IsSystemDefault && GetDefaultMicrophone() is null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(microphone),
                    "Windows has no active default microphone.");
            }

            activeInputDevice = microphone.IsSystemDefault
                ? null
                : ResolveInputDevice(microphone);

            var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(info => string.Equals(
                    info.Culture.TwoLetterISOLanguageName,
                    "en",
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException("No English Windows speech recognizer is installed.");

            var grammarPhrases = phrases
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
            recognizer.RecognizeCompleted += OnRecognitionCompleted;
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

            var recorderBuilder = new WasapiRecorderBuilder()
                .WithFormat(new WaveFormat(SampleRate, BitsPerSample, Channels))
                .WithBufferLength(100);
            if (microphone.IsSystemDefault)
            {
                recorderBuilder.WithDefaultDeviceStreamRouting();
            }
            else
            {
                recorderBuilder.WithDevice(activeInputDevice!);
            }

            recorder = await recorderBuilder.BuildAsync();
            recorder.DataAvailable += OnDataAvailable;
            recorder.RecordingStopped += OnRecordingStopped;

            recognitionCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.RecognizeAsync(RecognizeMode.Multiple);
            recognitionStarted = true;
            recorder.StartRecording();
            IsListening = true;
            started = true;
            WindowsLog.VoiceActivationStarted(logger, grammarPhrases.Length);
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Opening the selected microphone");
            throw new InvalidOperationException("Windows could not open the selected microphone.", exception);
        }
        finally
        {
            try
            {
                if (!started)
                {
                    await DisposeRecognitionResourcesAsync(recognitionStarted);
                }
            }
            finally
            {
                lifecycleLock.Release();
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsListening && recognizer is null && recorder is null)
            {
                WindowsLog.Debug(logger, "Voice activation was already stopped");
                return;
            }

            IsListening = false;
            await DisposeRecognitionResourcesAsync(recognitionStarted: true);
            WindowsLog.Information(logger, "Voice activation stopped and capture resources were released");
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
            WindowsLog.Debug(logger, "Voice recognition was already disposed");
            return;
        }

        await StopAsync();
        disposed = true;
        lifecycleLock.Dispose();
        WindowsLog.Debug(logger, "Voice recognition was disposed");
    }

    private void OnDataAvailable(
        ReadOnlySpan<byte> buffer,
        AudioClientBufferFlags _,
        long __,
        long ___)
    {
        if (IsListening)
        {
            audioStream?.Add(buffer);
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs eventArgs)
    {
        if (eventArgs.Result.Confidence >= MinimumConfidence)
        {
            WindowsLog.CommandRecognized(logger, eventArgs.Result.Confidence);
            TranscriptRecognized?.Invoke(
                this,
                new VoiceTranscriptEventArgs(eventArgs.Result.Text, eventArgs.Result.Confidence));
        }
    }

    private void OnSpeechRecognitionRejected(object? sender, SpeechRecognitionRejectedEventArgs eventArgs)
    {
        WindowsLog.Information(logger, "Speech recognition rejected audio that did not match the command grammar");
        RecognitionFailed?.Invoke(
            this,
            new VoiceRecognitionFailureEventArgs("I heard speech but could not match a supported command."));
    }

    private void OnRecognitionCompleted(object? sender, RecognizeCompletedEventArgs eventArgs) =>
        recognitionCompletion?.TrySetResult();

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            WindowsLog.Error(logger, eventArgs.Exception, "Capturing microphone audio");
            RecognitionFailed?.Invoke(
                this,
                new VoiceRecognitionFailureEventArgs($"Microphone capture stopped: {eventArgs.Exception.Message}"));
        }
    }

    private async Task DisposeRecognitionResourcesAsync(bool recognitionStarted)
    {
        var currentRecorder = recorder;
        var currentRecognizer = recognizer;
        var currentAudioStream = audioStream;
        var currentRecognitionCompletion = recognitionCompletion;

        try
        {
            if (currentRecorder is not null)
            {
                currentRecorder.DataAvailable -= OnDataAvailable;
            }

            currentAudioStream?.Complete();

            if (recognitionStarted && currentRecognizer is not null)
            {
                currentRecognizer.RecognizeAsyncCancel();
            }

            currentRecorder?.StopRecording();

            var recorderDisposal = currentRecorder is null
                ? ValueTask.CompletedTask
                : currentRecorder.DisposeAsync();
            var recognizerCompletion =
                recognitionStarted && currentRecognitionCompletion is not null
                    ? currentRecognitionCompletion.Task
                    : Task.CompletedTask;

            await VoiceRecognitionShutdown.WaitForCompletionAsync(
                recorderDisposal,
                recognizerCompletion);
        }
        finally
        {
            if (currentRecorder is not null)
            {
                currentRecorder.RecordingStopped -= OnRecordingStopped;
            }

            if (currentRecognizer is not null)
            {
                currentRecognizer.SpeechRecognized -= OnSpeechRecognized;
                currentRecognizer.SpeechRecognitionRejected -= OnSpeechRecognitionRejected;
                currentRecognizer.RecognizeCompleted -= OnRecognitionCompleted;
                currentRecognizer.Dispose();
            }

            if (currentAudioStream is not null)
            {
                await currentAudioStream.DisposeAsync();
            }

            activeInputDevice?.Dispose();

            recorder = null;
            recognizer = null;
            audioStream = null;
            activeInputDevice = null;
            recognitionCompletion = null;
        }
    }

    private MMDevice ResolveInputDevice(MicrophoneDevice microphone)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = enumerator.GetDevice(microphone.Id);
            if (endpoint.State == DeviceState.Active)
            {
                return endpoint;
            }

            endpoint.Dispose();
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Resolving the selected microphone");
        }

        throw new ArgumentOutOfRangeException(
            nameof(microphone),
            "The selected microphone is no longer available.");
    }
}