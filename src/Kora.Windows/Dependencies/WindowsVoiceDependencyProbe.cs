using System.Speech.Recognition;

using Kora.Core.Dependencies;

using NAudio.Wave;

namespace Kora.Windows.Dependencies;

public sealed class WindowsVoiceDependencyProbe : IDependencyProbe
{
    public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return Result(
                DependencyReadiness.Incompatible,
                "Kora voice support requires Windows 10 build 19041 or later.");
        }

        if (WaveIn.DeviceCount == 0)
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

        return Result(
            DependencyReadiness.NeedsConfiguration,
            $"{WaveIn.DeviceCount} microphone(s) detected. Select one and explicitly enable listening.");
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