using Kora.Core.Voice;

namespace Kora.Windows.Audio;

/// <summary>A model whose input and callback binding can be retired independently.</summary>
internal interface IWindowsActivatedRecognizer : IAsyncDisposable
{
    /// <summary>Reports speech for the current input binding.</summary>
    event EventHandler? SpeechDetected;
    /// <summary>Reports a final result without retaining it.</summary>
    event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
    /// <summary>Reports a provisional result without retaining it.</summary>
    event EventHandler<ActivatedHypothesisEventArgs>? Hypothesized;
    /// <summary>Reports failure of the current recognition.</summary>
    event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
    /// <summary>Confirms native recognition completion, separately from microphone release.</summary>
    event EventHandler<WindowsRecognitionCompletedEventArgs>? Completed;
    /// <summary>Binds a fresh, capture-owned input and callback generation.</summary>
    void BindInput(BlockingAudioStream stream);
    /// <summary>Starts one recognition on the bound input.</summary>
    void Start();
    /// <summary>Requests completion without claiming native quiescence.</summary>
    void Finish(bool cancel);
    /// <summary>Joins callbacks and detaches input after confirmed recognition completion.</summary>
    Task ResetAsync();
}
