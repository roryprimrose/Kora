using Microsoft.Extensions.Logging;

namespace Kora.Windows.Diagnostics;

internal static partial class WindowsLog
{
    [LoggerMessage(200, LogLevel.Debug, "{Operation}.")]
    public static partial void Debug(ILogger logger, string operation);

    [LoggerMessage(201, LogLevel.Information, "{Operation}.")]
    public static partial void Information(ILogger logger, string operation);

    [LoggerMessage(202, LogLevel.Warning, "{Operation}.")]
    public static partial void Warning(ILogger logger, string operation);

    [LoggerMessage(203, LogLevel.Error, "{Operation} failed.")]
    public static partial void Error(ILogger logger, Exception exception, string operation);

    [LoggerMessage(204, LogLevel.Debug, "Enumerated {DeviceCount} {DeviceType} devices.")]
    public static partial void DevicesEnumerated(ILogger logger, int deviceCount, string deviceType);

    [LoggerMessage(
        205,
        LogLevel.Information,
        "Voice activation started with {PhraseCount} grammar phrases.")]
    public static partial void VoiceActivationStarted(ILogger logger, int phraseCount);

    [LoggerMessage(
        206,
        LogLevel.Debug,
        "Accepted a recognized command with confidence {Confidence}.")]
    public static partial void CommandRecognized(ILogger logger, float confidence);

    [LoggerMessage(207, LogLevel.Information, "Speech synthesis and playback started.")]
    public static partial void SpeechStarted(ILogger logger);
}
