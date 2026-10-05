using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal interface IActivatedCaptureFactory
{
    Task<IActivatedCapture> OpenAsync(
        MicrophoneDevice microphone,
        IReadOnlyList<string> phrases,
        BlockingAudioStream stream,
        Func<bool> canOpen);
}
