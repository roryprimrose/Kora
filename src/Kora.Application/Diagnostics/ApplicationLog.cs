using Kora.Core.Commands;
using Kora.Core.Communication;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

internal static partial class ApplicationLog
{
    [LoggerMessage(100, LogLevel.Debug, "{Operation}.")]
    public static partial void Debug(ILogger logger, string operation);

    [LoggerMessage(101, LogLevel.Information, "{Operation}.")]
    public static partial void Information(ILogger logger, string operation);

    [LoggerMessage(102, LogLevel.Error, "{Operation} failed.")]
    public static partial void Error(ILogger logger, Exception exception, string operation);

    [LoggerMessage(103, LogLevel.Information, "Call state changed to {CallState}.")]
    public static partial void CallStateChanged(ILogger logger, CallState callState);

    [LoggerMessage(
        104,
        LogLevel.Information,
        "Environment refresh completed with {MicrophoneCount} microphones, {VoiceCount} voices, and {OutputDeviceCount} output devices.")]
    public static partial void EnvironmentRefreshCompleted(
        ILogger logger,
        int microphoneCount,
        int voiceCount,
        int outputDeviceCount);

    [LoggerMessage(105, LogLevel.Information, "Executing built-in action {BuiltInAction}.")]
    public static partial void BuiltInActionExecuting(ILogger logger, BuiltInAction builtInAction);

    [LoggerMessage(106, LogLevel.Debug, "Discovered {LogCount} application log files.")]
    public static partial void LogsDiscovered(ILogger logger, int logCount);

    [LoggerMessage(
        107,
        LogLevel.Information,
        "Read {CharacterCount} characters from application log {LogFileName}.")]
    public static partial void LogRead(
        ILogger logger,
        int characterCount,
        string logFileName);

    [LoggerMessage(108, LogLevel.Debug, "Loaded the saved {DeviceType} preference.")]
    public static partial void AudioDevicePreferenceLoaded(
        ILogger logger,
        string deviceType);

    [LoggerMessage(109, LogLevel.Information, "Saved the {DeviceType} preference.")]
    public static partial void AudioDevicePreferenceSaved(
        ILogger logger,
        string deviceType);

    [LoggerMessage(110, LogLevel.Debug, "Loaded the saved assistant name preference.")]
    public static partial void AssistantNamePreferenceLoaded(ILogger logger);

    [LoggerMessage(111, LogLevel.Information, "Saved the assistant name preference.")]
    public static partial void AssistantNamePreferenceSaved(ILogger logger);

    [LoggerMessage(112, LogLevel.Information, "Cleared the {DeviceType} override to use the Windows system default.")]
    public static partial void AudioDevicePreferenceCleared(
        ILogger logger,
        string deviceType);
}
