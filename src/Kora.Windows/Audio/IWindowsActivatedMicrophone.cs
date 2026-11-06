using Kora.Core.Voice;

namespace Kora.Windows.Audio;

/// <summary>One microphone/endpoint lifetime, never part of retained model state.</summary>
internal interface IWindowsActivatedMicrophone : IAsyncDisposable
{
    /// <summary>Supplies ephemeral audio for the current capture only.</summary>
    event WindowsCaptureAudioAvailable? DataAvailable;
    /// <summary>Reports an unsuccessful native recording.</summary>
    event EventHandler<VoiceRecognitionFailureEventArgs>? RecordingFailed;
    /// <summary>Starts the independently admitted microphone.</summary>
    void Start();
}
