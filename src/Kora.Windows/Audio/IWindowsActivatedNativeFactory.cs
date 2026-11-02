using Kora.Core.Voice;

namespace Kora.Windows.Audio;

/// <summary>The native boundary for activated model and microphone ownership.</summary>
internal interface IWindowsActivatedNativeFactory
{
    /// <summary>Gets exact-tuple qualification independently of implementation support.</summary>
    bool IsKeepLoadedQualified { get; }
    /// <summary>Revalidates native privacy before model creation and capture start.</summary>
    void RequireEligible(MicrophoneDevice microphone);
    /// <summary>Creates a model without opening or retaining an audio input.</summary>
    IWindowsActivatedRecognizer CreateRecognizer(IReadOnlyList<string> phrases, TimeSpan speechStartWindow);
    /// <summary>Opens one independently owned microphone lifetime.</summary>
    Task<IWindowsActivatedMicrophone> OpenMicrophoneAsync(MicrophoneDevice microphone, Func<bool> canOpen);
}
