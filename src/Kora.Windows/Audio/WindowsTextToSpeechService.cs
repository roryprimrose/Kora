using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;

using Kora.Core.Configuration;
using Kora.Core.Voice;
using Kora.Core.Diagnostics;
using Kora.Windows.Diagnostics;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Audio;

public sealed partial class WindowsTextToSpeechService : ITextToSpeechService, IPlaybackVolumeControl, IWindowsSpeechRateControl
{
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim speechLock = new(1, 1);
    private readonly SpeechSynthesizer synthesizer = new();
    private readonly Action<int> setOwnedWindowsGain;
    private readonly Action<int> setOwnedWindowsRate;
    private readonly Action cancelOwnedSynthesis;
    private TaskCompletionSource? synthesisCompletion;
    private TaskCompletionSource? playbackCompletion;
    private Prompt? activePrompt;
    private MemoryStream? audioStream;
    private WaveStream? audioReader;
    private SpeechOutputEnvelope? outputEnvelope;
    private WaveFormat? rawAudioFormat;
    private WasapiPlayer? playback;
    private MMDevice? activeOutputDevice;
    private bool followsSystemDefaultOutput;
    private bool isKokoroSynthesis;
    private bool stopRequested;
    private bool disposed;
    private long outputGeneration;
    private PlaybackVolume? playbackVolume = PlaybackVolume.Default;
    private WindowsSpeechRate? windowsSpeechRate = WindowsSpeechRate.Default;
    private long playbackGeneration;
    private Guid? currentPlaybackId;
    private Func<HostActivity>? beginStopReceipt;
    private readonly KokoroTextToSpeechProvider? kokoroProvider;
    private readonly ILogger<WindowsTextToSpeechService> logger;

    public WindowsTextToSpeechService(ILogger<WindowsTextToSpeechService> logger)
        : this(kokoroProvider: null, logger)
    {
    }

    public WindowsTextToSpeechService(
        KokoroTextToSpeechProvider? kokoroProvider,
        ILogger<WindowsTextToSpeechService> logger)
        : this(kokoroProvider, logger, setOwnedWindowsGain: null)
    {
    }

    internal WindowsTextToSpeechService(
        KokoroTextToSpeechProvider? kokoroProvider,
        ILogger<WindowsTextToSpeechService> logger,
        Action<int>? setOwnedWindowsGain,
        Action? cancelOwnedSynthesis = null,
        Action<int>? setOwnedWindowsRate = null)
    {
        this.kokoroProvider = kokoroProvider;
        this.logger = logger;
        this.setOwnedWindowsGain = setOwnedWindowsGain ?? (percent => synthesizer.Volume = percent);
        this.setOwnedWindowsRate = setOwnedWindowsRate ?? (rate => synthesizer.Rate = rate);
        this.cancelOwnedSynthesis = cancelOwnedSynthesis ?? synthesizer.SpeakAsyncCancelAll;
        synthesizer.SpeakCompleted += OnSpeakCompleted;
    }

    internal void ApplyOwnedWindowsGain(PlaybackVolume volume) => setOwnedWindowsGain(volume.Percent);

    internal void ApplyOwnedWindowsRate(string providerId)
    {
        if (string.Equals(providerId, SpeechProviderIds.Kokoro, StringComparison.Ordinal)) { return; }
        if (!string.Equals(providerId, SpeechProviderIds.Windows, StringComparison.Ordinal))
        {
            throw new WindowsSpeechRateUnavailableException("The provider has no qualified Windows-native rate capability.");
        }
        setOwnedWindowsRate((windowsSpeechRate
            ?? throw new WindowsSpeechRateUnavailableException("Windows speech rate is unconfirmed; full visual output is required.")).Value);
    }

    internal void StartOwnedWindowsSynthesis(PlaybackVolume volume, Action synthesize)
    {
        lock (stateLock)
        {
            ApplyOwnedWindowsGain(volume);
            try { ApplyOwnedWindowsRate(SpeechProviderIds.Windows); }
            catch (Exception exception)
            {
                WindowsLog.Error(logger, exception, "Applying Kora-owned Windows synthesis rate");
                throw new WindowsSpeechRateUnavailableException(
                    "The Kora-owned Windows rate could not be applied. Full visual output is retained; explicitly refresh the rate preference.",
                    exception);
            }
            synthesize();
        }
    }

    public bool IsSpeaking
    {
        get
        {
            lock (stateLock)
            {
                return activePrompt is not null
                    || isKokoroSynthesis
                    || playback is not null;
            }
        }
    }

    public SpeechPlaybackFrame PlaybackFrame
    {
        get
        {
            lock (stateLock)
            {
                if (stopRequested
                    || playback is null
                    || playbackCompletion is null
                    || playback.PlaybackState != PlaybackState.Playing
                    || outputEnvelope is null)
                {
                    return SpeechPlaybackFrame.Inactive;
                }

                try
                {
                    // Device position, not reader position, keeps the envelope behind buffered audio.
                    var position = TimeSpan.FromSeconds(
                        playback.GetPosition() / (double)playback.OutputWaveFormat.AverageBytesPerSecond);
                    return new SpeechPlaybackFrame(true, outputEnvelope.GetLevel(position),
                        currentPlaybackId, playbackGeneration);
                }
                catch (COMException exception)
                {
                    throw new AudioOutputDeviceUnavailableException(
                        AudioOutputFailureReason.PlaybackFailed,
                        "Windows could not read the speech playback position.",
                        exception);
                }
            }
        }
    }

    public IReadOnlyList<SpeechProvider> GetProviders()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var windowsDefaultVoiceId = GetDefaultVoice()?.Id;
        var providers = new List<SpeechProvider>
        {
            new(
                SpeechProviderIds.Windows,
                "Windows",
                "Uses the speech voices installed with Windows.",
                IsInstalled: true,
                IsBuiltIn: true,
                DownloadSizeBytes: null,
                DefaultVoiceId: windowsDefaultVoiceId)
            {
                RateSupport = SpeechRateSupport.WindowsNative,
            },
        };
        if (kokoroProvider is not null)
        {
            providers.Add(new SpeechProvider(
                SpeechProviderIds.Kokoro,
                "Kokoro",
                "Uses a more natural neural voice entirely on this device.",
                kokoroProvider.IsInstalled,
                IsBuiltIn: false,
                DownloadSizeBytes: KokoroTextToSpeechProvider.DownloadSizeBytes,
                DefaultVoiceId: "af_heart"));
        }

        return providers;
    }

    public IReadOnlyList<SpeechVoice> GetVoices()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var voices = GetEnabledVoices(synthesizer)
            .Select(installedVoice => ToSpeechVoice(installedVoice.VoiceInfo))
            .Concat(kokoroProvider?.GetVoices() ?? [])
            .OrderBy(voice => voice.ProviderId, StringComparer.Ordinal)
            .ThenBy(voice => voice.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        WindowsLog.DevicesEnumerated(logger, voices.Length, "speech voice");
        return voices;
    }

    public SpeechVoice? GetDefaultVoice() =>
        SelectDefaultWindowsVoice(GetVoices(), CultureInfo.CurrentUICulture);

    internal static SpeechVoice? SelectDefaultWindowsVoice(IEnumerable<SpeechVoice> voices, CultureInfo culture) =>
        SpeechVoiceSelector.SelectDefault(voices.Where(voice =>
            string.Equals(voice.ProviderId, SpeechProviderIds.Windows, StringComparison.Ordinal)), culture);

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var devices = new List<AudioOutputDevice>(endpoints.Count);
            foreach (var endpoint in endpoints)
            {
                using (endpoint)
                {
                    devices.Add(new AudioOutputDevice(
                        endpoint.ID,
                        endpoint.FriendlyName,
                        IsEndpointMuted(endpoint)));
                }
            }

            var result = devices
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            WindowsLog.DevicesEnumerated(logger, result.Length, "audio output");
            return result;
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Enumerating Windows audio output");
            throw new AudioOutputDeviceUnavailableException(
                "Windows could not enumerate audio output devices.",
                exception);
        }
    }

    public AudioOutputDevice? GetDefaultOutputDevice()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return endpoint.State == DeviceState.Active
                ? new AudioOutputDevice(
                    endpoint.ID,
                    endpoint.FriendlyName,
                    IsEndpointMuted(endpoint))
                : null;
        }
        catch (COMException)
        {
            WindowsLog.Warning(logger, "No active Windows multimedia output endpoint is available");
            return null;
        }
    }

    public Task SpeakAsync(
        string text,
        SpeechVoice voice,
        AudioOutputDevice outputDevice,
        CancellationToken cancellationToken = default)
        => RunCurrentOutputAsync((generation, token) => SpeakCoreAsync(text, voice, outputDevice, generation, null, token), cancellationToken);

    public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice,
        Guid playbackId, CancellationToken cancellationToken = default)
    {
        if (playbackId == Guid.Empty) { throw new ArgumentException("Playback identity must be host-owned and nonempty.", nameof(playbackId)); }
        return RunCurrentOutputAsync((generation, token) =>
            SpeakCoreAsync(text, voice, outputDevice, generation, playbackId, token), cancellationToken);
    }

    private async Task SpeakCoreAsync(
        string text,
        SpeechVoice voice,
        AudioOutputDevice outputDevice,
        long generation,
        Guid? playbackId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(outputDevice);

        PlaybackVolume volume;
        lock (stateLock)
        {
            volume = playbackVolume is { AllowsSpeech: true } current ? current
                : throw new PlaybackVolumeUnavailableException(
                    "Kora playback volume is zero or unavailable. Full visual output is required; no synthesis started.");
            if (string.Equals(voice.ProviderId, SpeechProviderIds.Windows, StringComparison.Ordinal) && windowsSpeechRate is null)
            {
                throw new WindowsSpeechRateUnavailableException("Windows speech rate is unconfirmed; no synthesis or device access started.");
            }
        }
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task synthesisTask;
        try
        {
            if (IsSpeaking)
            {
                throw new InvalidOperationException("Speech playback is already active.");
            }

            lock (stateLock)
            {
                if (generation != Interlocked.Read(ref outputGeneration))
                {
                    throw new OperationCanceledException("Speech output was invalidated by a privacy event.");
                }
                stopRequested = false;
                playbackGeneration = generation;
                currentPlaybackId = playbackId;
            }

            followsSystemDefaultOutput = outputDevice.IsSystemDefault;
            if (followsSystemDefaultOutput)
            {
                var systemOutput = GetDefaultOutputDevice()
                    ?? throw new AudioOutputDeviceUnavailableException(
                        "Windows has no active default audio output device.");
                if (systemOutput.IsMuted)
                {
                    throw new AudioOutputDeviceUnavailableException(
                        AudioOutputFailureReason.Muted,
                        "The Windows default audio output device is muted or its volume is zero.");
                }
            }
            else
            {
                activeOutputDevice = ResolveOutputDevice(outputDevice);
            }

            if (!GetVoices().Any(candidate =>
                    string.Equals(candidate.Id, voice.Id, StringComparison.Ordinal)
                    && string.Equals(
                        candidate.ProviderId,
                        voice.ProviderId,
                        StringComparison.Ordinal)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voice),
                    "The selected speech voice is no longer installed.");
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (string.Equals(
                    voice.ProviderId,
                    SpeechProviderIds.Kokoro,
                    StringComparison.Ordinal))
            {
                lock (stateLock)
                {
                    synthesisCompletion = completion;
                    isKokoroSynthesis = true;
                }

                synthesisTask = SynthesizeKokoroAsync(
                    text,
                    voice,
                    volume,
                    completion,
                    cancellationToken);
            }
            else
            {
                if (!string.Equals(voice.ProviderId, SpeechProviderIds.Windows, StringComparison.Ordinal))
                {
                    throw new PlaybackVolumeUnavailableException("This provider has no qualified Kora-only volume capability.");
                }
                synthesizer.SelectVoice(voice.Id);
                audioStream = new MemoryStream();
                synthesizer.SetOutputToWaveStream(audioStream);
                var prompt = new Prompt(text);
                lock (stateLock)
                {
                    RequireCurrentOutput(generation);
                    synthesisCompletion = completion;
                    activePrompt = prompt;
                    StartOwnedWindowsSynthesis(volume, () => synthesizer.SpeakAsync(prompt));
                }
                synthesisTask = completion.Task;
            }

            WindowsLog.SpeechStarted(logger);
        }
        catch
        {
            ResetSynthesisState();
            synthesizer.SetOutputToDefaultAudioDevice();
            DisposePlaybackResources();
            throw;
        }
        finally
        {
            lifecycleLock.Release();
        }

        try
        {
            await synthesisTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!IsStopRequested() && generation == Interlocked.Read(ref outputGeneration))
            {
                await StartPlaybackAsync(generation, cancellationToken).ConfigureAwait(false);
            }
            if (generation != Interlocked.Read(ref outputGeneration))
            {
                throw new OperationCanceledException("Speech output route was invalidated.");
            }
        }
        catch (OperationCanceledException)
        {
            WindowsLog.Information(logger, "Speech output was cancelled");
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await lifecycleLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                synthesizer.SetOutputToDefaultAudioDevice();
                DisposePlaybackResources();
                lock (stateLock)
                {
                    stopRequested = false;
                }
                WindowsLog.Debug(logger, "Speech output resources were released");
            }
            finally
            {
                lifecycleLock.Release();
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        var requested = Stopwatch.GetTimestamp();
        WindowsLog.Debug(logger, "Stopping speech output");
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task? completionTask;
        long stoppedGeneration;
        bool hadOutput;
        try
        {
            lock (stateLock)
            {
                stopRequested = true;
                stoppedGeneration = playbackGeneration;
                hadOutput = activePrompt is not null || isKokoroSynthesis || playback is not null;
                completionTask = synthesisCompletion?.Task ?? playbackCompletion?.Task;
                beginStopReceipt = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
            }

            cancelOwnedSynthesis();
            playback?.Stop();
        }
        finally
        {
            lifecycleLock.Release();
        }

        if (completionTask is not null)
        {
            await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        WindowsLog.OutputStopCompleted(logger, stoppedGeneration, hadOutput, requested,
            Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        activity.Complete(HostOperationOutcome.Completed);
    }

    public void InvalidateOutput()
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        WasapiPlayer? player;
        lock (stateLock)
        {
            player = RetireOutputLocked();
            beginStopReceipt = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
        }
        StopRetiredOutput(player);
        activity.Complete(HostOperationOutcome.Completed);
    }

    public void SetPlaybackVolume(PlaybackVolume? volume)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WasapiPlayer? player;
        lock (stateLock)
        {
            if (playbackVolume == volume) { return; }
            playbackVolume = volume;
            player = RetireOutputLocked();
        }
        StopRetiredOutput(player);
    }

    public void SetWindowsSpeechRate(WindowsSpeechRate? rate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WasapiPlayer? player;
        lock (stateLock)
        {
            if (windowsSpeechRate == rate) { return; }
            windowsSpeechRate = rate;
            player = RetireOutputLocked();
        }
        StopRetiredOutput(player);
    }

    private WasapiPlayer? RetireOutputLocked()
    {
        Interlocked.Increment(ref outputGeneration);
        currentPlaybackId = null;
        stopRequested = true;
        return playback;
    }

    private void StopRetiredOutput(WasapiPlayer? player)
    {
        // Native stop can synchronously wait for callbacks that need stateLock.
        player?.Stop();
        cancelOwnedSynthesis();
    }

    public async Task InstallProviderAsync(
        string providerId,
        IProgress<SpeechProviderInstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!string.Equals(
                providerId,
                SpeechProviderIds.Kokoro,
                StringComparison.Ordinal)
            || kokoroProvider is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerId),
                providerId,
                "The speech provider cannot be installed.");
        }

        await kokoroProvider.InstallAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!string.Equals(
                providerId,
                SpeechProviderIds.Kokoro,
                StringComparison.Ordinal)
            || kokoroProvider is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerId),
                providerId,
                "The speech provider cannot be removed.");
        }

        if (IsSpeaking)
        {
            throw new InvalidOperationException(
                "Stop speech playback before removing its provider.");
        }

        await kokoroProvider.RemoveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            WindowsLog.Debug(logger, "Text-to-speech was already disposed");
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await speechLock.WaitAsync().ConfigureAwait(false);
        disposed = true;
        try
        {
            synthesizer.SpeakCompleted -= OnSpeakCompleted;
            synthesizer.Dispose();
            DisposePlaybackResources();
            lifecycleLock.Dispose();
            WindowsLog.Debug(logger, "Text-to-speech was disposed");
        }
        finally
        {
            speechLock.Release();
            speechLock.Dispose();
        }
    }

    internal static IReadOnlyList<InstalledVoice> GetEnabledVoices(SpeechSynthesizer synthesizer)
    {
#pragma warning disable CA1304, MA0011 // Voice selection must show every installed culture.
        return synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled)
            .ToArray();
#pragma warning restore CA1304, MA0011
    }

    internal static SpeechVoice ToSpeechVoice(VoiceInfo voice) =>
        new(
            voice.Name,
            voice.Name,
            voice.Culture.Name,
            voice.Gender switch
            {
                VoiceGender.Female => SpeechVoiceGender.Female,
                VoiceGender.Male => SpeechVoiceGender.Male,
                VoiceGender.Neutral => SpeechVoiceGender.Neutral,
                _ => SpeechVoiceGender.Unknown,
            });

    private static MMDevice ResolveOutputDevice(AudioOutputDevice outputDevice)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = enumerator.GetDevice(outputDevice.Id);
            if (endpoint.State != DeviceState.Active)
            {
                endpoint.Dispose();
                throw new AudioOutputDeviceUnavailableException(
                    "The selected audio output device is no longer active.");
            }

            if (IsEndpointMuted(endpoint))
            {
                endpoint.Dispose();
                throw new AudioOutputDeviceUnavailableException(
                    AudioOutputFailureReason.Muted,
                    "The selected audio output device is muted or its Windows volume is zero.");
            }

            return endpoint;
        }
        catch (COMException exception)
        {
            throw new AudioOutputDeviceUnavailableException(
                "The selected audio output device is no longer available.",
                exception);
        }
    }

    private async Task StartPlaybackAsync(long generation, CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task completionTask;
        try
        {
            if (IsStopRequested() || generation != Interlocked.Read(ref outputGeneration))
            {
                return;
            }

            var stream = audioStream
                ?? throw new InvalidOperationException("Windows speech synthesis produced no audio stream.");
            stream.Position = 0;
            audioReader = rawAudioFormat is null
                ? new WaveFileReader(stream)
                : new RawSourceWaveStream(stream, rawAudioFormat);
            ValidatePlaybackSamples(audioReader);
            var envelope = SpeechOutputEnvelope.Create(audioReader, cancellationToken);
            lock (stateLock)
            {
                outputEnvelope = envelope;
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var playbackBuilder = new WasapiPlayerBuilder()
                    .WithSharedMode()
                    .WithEventSync()
                    .WithLatency(100);
                if (followsSystemDefaultOutput)
                {
                    playbackBuilder.WithDefaultDeviceStreamRouting();
                }
                else
                {
                    playbackBuilder.WithDevice(
                        activeOutputDevice
                        ?? throw new AudioOutputDeviceUnavailableException(
                            "No audio output device is selected."));
                }

                var player = await playbackBuilder.BuildAsync().ConfigureAwait(false);
                lock (stateLock)
                {
                    playback = player;
                    playbackGeneration = generation;
                    beginStopReceipt = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
                    playback.PlaybackStopped += OnPlaybackStopped;
                    playbackCompletion = completion;
                    if (stopRequested || generation != Interlocked.Read(ref outputGeneration))
                    {
                        completion.TrySetResult();
                    }
                    else
                    {
                        playback.Init(audioReader);
                        playback.Play();
                    }
                }
            }
            catch (Exception exception) when (
                exception is COMException or InvalidOperationException)
            {
                throw new AudioOutputDeviceUnavailableException(
                    AudioOutputFailureReason.PlaybackFailed,
                    "Windows could not open the selected audio output device.",
                    exception);
            }

            completionTask = completion.Task;
        }
        finally
        {
            lifecycleLock.Release();
        }

        await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs eventArgs)
    {
        TaskCompletionSource? completion;
        lock (stateLock)
        {
            if (!ReferenceEquals(activePrompt, eventArgs.Prompt))
            {
                return;
            }

            completion = synthesisCompletion;
            synthesisCompletion = null;
            activePrompt = null;
        }

        if (eventArgs.Error is not null)
        {
            completion?.TrySetException(
                new InvalidOperationException("Windows speech synthesis failed.", eventArgs.Error));
        }
        else
        {
            completion?.TrySetResult();
        }
    }

    internal void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        var stopped = Stopwatch.GetTimestamp();
        TaskCompletionSource? completion;
        long retiredGeneration;
        Func<HostActivity>? beginReceipt;
        lock (stateLock)
        {
            if (sender is null || !ReferenceEquals(sender, playback)) { return; }
            completion = playbackCompletion;
            retiredGeneration = playbackGeneration;
            beginReceipt = beginStopReceipt;
            playbackCompletion = null;
            currentPlaybackId = null;
            outputEnvelope?.Clear();
            outputEnvelope = null;
        }
        using var activity = beginReceipt is not null ? beginReceipt()
            : HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);

        if (eventArgs.Exception is not null)
        {
            completion?.TrySetException(
                new AudioOutputDeviceUnavailableException(
                    AudioOutputFailureReason.PlaybackFailed,
                    "The selected audio output device failed during playback.",
                    eventArgs.Exception));
        }
        else
        {
            completion?.TrySetResult();
        }
        WindowsLog.NativePlaybackStopped(logger, retiredGeneration, stopped);
        activity.Complete(eventArgs.Exception is null ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    private void ResetSynthesisState()
    {
        lock (stateLock)
        {
            synthesisCompletion = null;
            activePrompt = null;
            isKokoroSynthesis = false;
        }
    }

    private async Task SynthesizeKokoroAsync(
        string text,
        SpeechVoice voice,
        PlaybackVolume volume,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = kokoroProvider
                ?? throw new InvalidOperationException(
                    "The Kokoro speech provider is unavailable.");
            var audio = await provider.SynthesizeAsync(
                text,
                voice.Id,
                cancellationToken).ConfigureAwait(false);
            audioStream = CreateOutputAudioStream(audio.Samples);
            if (audio.BitsPerSample != 16 || audio.Channels != 1 || audio.SampleRate <= 0)
            {
                Array.Clear(audio.Samples);
                throw new InvalidDataException("Kokoro produced an unsupported PCM format. Full visual output is required.");
            }
            Pcm16PlaybackGain.Attenuate(audio.Samples, volume);
            rawAudioFormat = new WaveFormat(
                audio.SampleRate,
                audio.BitsPerSample,
                audio.Channels);
            completion.TrySetResult();
        }
        finally
        {
            completion.TrySetCanceled(CancellationToken.None);
            lock (stateLock)
            {
                synthesisCompletion = null;
                isKokoroSynthesis = false;
            }
        }
    }

    private bool IsStopRequested()
    {
        lock (stateLock)
        {
            return stopRequested;
        }
    }

    private static bool IsEndpointMuted(MMDevice endpoint) =>
        endpoint.AudioEndpointVolume.Mute
        || endpoint.AudioEndpointVolume.MasterVolumeLevelScalar <= 0;

    internal static MemoryStream CreateOutputAudioStream(byte[] samples) =>
        new(samples, 0, samples.Length, writable: false, publiclyVisible: true);

    internal static void ValidatePlaybackSamples(WaveStream reader)
    {
        if (reader.Length == 0 || reader.Length % reader.WaveFormat.BlockAlign != 0)
        {
            throw new InvalidDataException("Speech audio must contain complete nonempty PCM frames; no silent-success playback is claimed.");
        }
    }

    internal static void ClearOutputAudio(MemoryStream stream) =>
        stream.GetBuffer().AsSpan().Clear();

    private void DisposePlaybackResources()
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        var audioBytes = audioStream?.Capacity ?? 0;
        var hadResources = playback is not null || audioStream is not null;
        WasapiPlayer? player;
        lock (stateLock)
        {
            player = playback;
            currentPlaybackId = null;
            if (player is not null)
            {
                player.PlaybackStopped -= OnPlaybackStopped;
                playback = null;
            }
            playbackCompletion = null;
            outputEnvelope?.Clear();
            outputEnvelope = null;
        }
        player?.Dispose();

        audioReader?.Dispose();
        audioReader = null;
        rawAudioFormat = null;
        if (audioStream is not null)
        {
            ClearOutputAudio(audioStream);
        }
        audioStream?.Dispose();
        audioStream = null;
        activeOutputDevice?.Dispose();
        activeOutputDevice = null;
        followsSystemDefaultOutput = false;
        if (hadResources)
        {
            WindowsLog.OutputResourcesReleased(logger, playbackGeneration, Stopwatch.GetTimestamp(), audioBytes);
        }
        activity.Complete(HostOperationOutcome.Completed);
    }
}