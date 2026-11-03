using Kora.Core.Voice;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kora.Windows.Audio;

internal sealed class WindowsActivatedCaptureFactory : IActivatedCaptureFactory
{
    private readonly IWindowsActivatedNativeFactory native;
    private readonly WindowsRecognizerResidency residency;
    private IWindowsActivatedMicrophone? quarantinedMicrophone;

    public WindowsActivatedCaptureFactory() : this(new WindowsActivatedNativeFactory()) { }

    internal WindowsActivatedCaptureFactory(IWindowsActivatedNativeFactory native)
    {
        this.native = native;
        residency = new(native);
    }

    public bool IsAvailable => residency.IsAvailable && Volatile.Read(ref quarantinedMicrophone) is null;
    public bool IsQuiescent => residency.IsQuiescent;
    public Task RetireAsync() => residency.RetireAsync();

    public string? GetUnavailableReason(SpeechInputSelection selection)
    {
        selection.Validate();
        return selection.ProfileId is SpeechInputProfileCatalogue.WindowsProfileId
            && selection.Resource == SpeechInputResource.WindowsManaged
            && selection.Residency == SpeechInputResidency.KeepLoaded && native.IsKeepLoadedQualified
            ? null : SpeechInputProfileCatalogue.UnavailableReason(selection);
    }

    public Task<IActivatedCapture> OpenAsync(
        MicrophoneDevice microphone,
        IReadOnlyList<string> phrases,
        BlockingAudioStream stream,
        Func<bool> canOpen) =>
        OpenAsync(microphone, phrases, stream, canOpen, VoiceCaptureLimits.Default.EmptySpeechDeadline);

    public Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone,
        IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow) =>
        OpenAsync(microphone, phrases, stream, canOpen, speechStartWindow, new(0, SpeechInputSelection.Default));

    public async Task<IActivatedCapture> OpenAsync(MicrophoneDevice microphone,
        IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen, TimeSpan speechStartWindow,
        SpeechInputGeneration profile)
    {
        if (GetUnavailableReason(profile.Selection) is { } reason) { throw new InvalidOperationException(reason); }
        native.RequireEligible(microphone);
        if (!canOpen()) { throw new OperationCanceledException("Capture admission was retired before model acquisition."); }
        var lease = await residency.AcquireAsync(profile, phrases, speechStartWindow, canOpen).ConfigureAwait(false);
        IWindowsActivatedMicrophone? recorder = null;
        try
        {
            lease.Recognizer.BindInput(stream);
            recorder = await native.OpenMicrophoneAsync(microphone, canOpen).ConfigureAwait(false);
            if (!canOpen()) { throw new OperationCanceledException("Capture admission changed during microphone open."); }
            return new WindowsActivatedCapture(lease, recorder, stream, () => native.RequireEligible(microphone), canOpen);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lease.Quarantine(exception);
            stream.ClearAndComplete();
            try
            {
                if (recorder is not null)
                {
                    Volatile.Write(ref quarantinedMicrophone, recorder);
                    await recorder.DisposeAsync().ConfigureAwait(false);
                    Volatile.Write(ref quarantinedMicrophone, null);
                }
            }
            finally
            {
                await lease.Recognizer.ResetAsync().ConfigureAwait(false);
                await stream.DisposeAsync().ConfigureAwait(false);
                await lease.DiscardAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    internal static async Task<IActivatedCapture> OpenCommandAsync(
        MicrophoneDevice microphone, IReadOnlyList<string> phrases, BlockingAudioStream stream, Func<bool> canOpen)
    {
        if (!canOpen()) { throw new OperationCanceledException("Command audio admission was retired."); }
        var commandNative = new WindowsActivatedNativeFactory();
        commandNative.RequireEligible(microphone);
        var recognizer = commandNative.CreateRecognizer(phrases, VoiceCaptureLimits.Default.EmptySpeechDeadline);
        try
        {
            recognizer.BindInput(stream);
            return new WindowsActivatedCapture(new(recognizer, null), null, stream,
                () => commandNative.RequireEligible(microphone), canOpen);
        }
        catch
        {
            await recognizer.DisposeAsync().ConfigureAwait(false);
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
