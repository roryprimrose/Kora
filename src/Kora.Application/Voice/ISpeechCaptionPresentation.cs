using Kora.Application.Infrastructure;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>The passive, already-admitted caption presentation consumed by the desktop.</summary>
public interface ISpeechCaptionPresentation
{
    /// <summary>Gets the currently observed private text, or <c>null</c>.</summary>
    string? SpeechCaptionText { get; }
    /// <summary>Gets the current or successfully retained utterance label.</summary>
    string SpeechCaptionLabel { get; }
    /// <summary>Gets the pin control label.</summary>
    string SpeechCaptionPinLabel { get; }
    /// <summary>Gets the existing admitted pin command.</summary>
    AsyncCommand ToggleSpeechCaptionPinCommand { get; }
    /// <summary>Gets the confirmed corner, or <c>null</c>.</summary>
    SpeechCaptionPlacement? SpeechCaptionPlacement { get; }
    /// <summary>Gets whether already-observed caption content may be revealed.</summary>
    bool IsSpeechCaptionVisible { get; }
    /// <summary>Gets whether the current owning native user may change passive placement.</summary>
    bool CanChooseSpeechCaptionDisplay { get; }
    /// <summary>Occurs when private native surfaces must close.</summary>
    event EventHandler? PrivacyClosureRequested;
    /// <summary>Revalidates the existing actual playback; does not start output.</summary>
    void RefreshSpeechPlaybackFrame();
    /// <summary>Retires the source and reports display recovery without repairing preferences.</summary>
    /// <param name="reportRecovery">Whether to publish the initial content-free recovery notice.</param>
    void ReportSpeechCaptionDisplayUnavailable(bool reportRecovery = true);
    /// <summary>Retires presentation after a native failure.</summary>
    /// <param name="exceptionType">The content-free failure type.</param>
    void ReportSpeechCaptionPresentationFailure(string exceptionType);
}
