using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class SpeechInputConfigurationService
{
    [LoggerMessage(5801, LogLevel.Error, "Pre-host startup speech-input preference unavailable.")]
    private partial void StartupPreferenceUnavailable(Exception exception);

    [LoggerMessage(5802, LogLevel.Information, "Speech-input configuration generation {Generation} committed; available {Available}.")]
    private partial void SelectionCommitted(long generation, bool available);

    [LoggerMessage(5803, LogLevel.Error, "Speech-input selection failed; capture held unavailable.")]
    private partial void SelectionFailed(Exception exception);

    [LoggerMessage(5804, LogLevel.Information, "Speech-input selection cancelled; capture not restarted.")]
    private partial void SelectionCancelled();
}
