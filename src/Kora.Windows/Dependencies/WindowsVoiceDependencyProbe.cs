using System.Runtime.InteropServices;
using System.Speech.Recognition;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using NAudio.CoreAudioApi;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed class WindowsVoiceDependencyProbe(
    IMicrophoneAccessService microphoneAccess,
    ILogger<WindowsVoiceDependencyProbe> logger) : IDependencyProbe
{
    public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WindowsLog.Debug(logger, "Probing Windows microphone and recognition readiness");

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return Result(
                DependencyReadiness.Incompatible,
                "Voice support requires Windows 10 build 19041 or later.");
        }

        var access = microphoneAccess.GetStatus();
        if (access.State == MicrophoneAccessState.Denied)
        {
            return Result(
                DependencyReadiness.NeedsConfiguration,
                access.Detail);
        }

        int microphoneCount;
        bool hasDefaultMicrophone;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var microphones = enumerator.EnumerateAudioEndPoints(
                DataFlow.Capture,
                DeviceState.Active);
            microphoneCount = microphones.Count;
            foreach (var microphone in microphones)
            {
                microphone.Dispose();
            }

            hasDefaultMicrophone = false;
            if (microphoneCount > 0)
            {
                try
                {
                    using var defaultMicrophone = enumerator.GetDefaultAudioEndpoint(
                        DataFlow.Capture,
                        Role.Multimedia);
                    hasDefaultMicrophone = defaultMicrophone.State == DeviceState.Active;
                }
                catch (COMException)
                {
                    hasDefaultMicrophone = false;
                }
            }
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Probing Windows microphone readiness");
            throw new InvalidOperationException(
                "Windows could not inspect microphone input devices.",
                exception);
        }

        if (microphoneCount == 0)
        {
            return Result(
                DependencyReadiness.Missing,
                "No microphone capture device was detected.");
        }

        if (!SpeechRecognitionEngine.InstalledRecognizers()
                .Any(info => string.Equals(
                    info.Culture.TwoLetterISOLanguageName,
                    "en",
                    StringComparison.Ordinal)))
        {
            return Result(
                DependencyReadiness.Missing,
                "Install an English Windows speech recognition language before enabling listening.");
        }

        if (!hasDefaultMicrophone)
        {
            return Result(
                DependencyReadiness.NeedsConfiguration,
                $"{microphoneCount} microphone(s) detected, but no active Windows multimedia default is selected. Select an application microphone.");
        }

        return Result(
            access.State == MicrophoneAccessState.Allowed
                ? DependencyReadiness.Ready
                : DependencyReadiness.NeedsConfiguration,
            $"{microphoneCount} microphone(s) detected. {access.Detail}");
    }

    private static ValueTask<DependencyStatus> Result(
        DependencyReadiness readiness,
        string detail) =>
        ValueTask.FromResult(new DependencyStatus(
            "windows.voice",
            "Windows microphone and speech",
            readiness,
            detail));
}