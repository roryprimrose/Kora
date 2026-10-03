using System.Globalization;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Diagnostics;

using NAudio.CoreAudioApi;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

public sealed class WindowsTextToSpeechDependencyProbe(
    ILogger<WindowsTextToSpeechDependencyProbe> logger) : IDependencyProbe
{
    public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WindowsLog.Debug(logger, "Probing Windows text-to-speech and output readiness");

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return ValueTask.FromResult(CreateStatus(
                DependencyReadiness.Incompatible,
                "Speech output requires Windows 10 build 19041 or later."));
        }

        try
        {
            DependencyStatus status;
            using (var synthesizer = new SpeechSynthesizer())
            using (var enumerator = new MMDeviceEnumerator())
            {
                var voices = WindowsTextToSpeechService.GetEnabledVoices(synthesizer)
                    .Select(voice => WindowsTextToSpeechService.ToSpeechVoice(voice.VoiceInfo))
                    .ToArray();

                var outputDevices = enumerator
                    .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                var outputDeviceCount = outputDevices.Count;
                foreach (var outputDevice in outputDevices)
                {
                    outputDevice.Dispose();
                }

                var hasDefaultOutputDevice = false;
                var isDefaultOutputMuted = false;
                if (outputDeviceCount > 0)
                {
                    try
                    {
                        using var defaultOutputDevice = enumerator.GetDefaultAudioEndpoint(
                            DataFlow.Render,
                            Role.Multimedia);
                        hasDefaultOutputDevice = defaultOutputDevice.State == DeviceState.Active;
                        isDefaultOutputMuted = defaultOutputDevice.AudioEndpointVolume.Mute
                            || defaultOutputDevice.AudioEndpointVolume.MasterVolumeLevelScalar <= 0;
                    }
                    catch (COMException)
                    {
                        hasDefaultOutputDevice = false;
                    }
                }

                if (outputDeviceCount == 0)
                {
                    status = CreateStatus(
                        DependencyReadiness.Missing,
                        "No Windows audio output device is available. Connect or enable an output device before using speech.");
                }
                else if (voices.Length == 0)
                {
                    status = CreateStatus(
                        DependencyReadiness.Missing,
                        "Install a Windows text-to-speech voice before enabling spoken responses.");
                }
                else if (!hasDefaultOutputDevice)
                {
                    status = CreateStatus(
                        DependencyReadiness.NeedsConfiguration,
                        $"{outputDeviceCount} audio output device(s) detected, but no active multimedia default is selected. Select an application output device.");
                }
                else if (isDefaultOutputMuted)
                {
                    status = CreateStatus(
                        DependencyReadiness.NeedsConfiguration,
                        "The default audio output device is muted or its Windows volume is zero. Visual responses remain available.");
                }
                else
                {
                    var defaultVoice = SpeechVoiceSelector.SelectDefault(
                        voices,
                        CultureInfo.CurrentUICulture);
                    status = defaultVoice is null
                        ? CreateStatus(
                            DependencyReadiness.NeedsConfiguration,
                            $"{voices.Length} voice(s) detected, but none match {CultureInfo.CurrentUICulture.DisplayName}. Select an available voice explicitly or install a compatible Windows speech pack.")
                        : CreateStatus(
                            DependencyReadiness.Ready,
                            $"{voices.Length} voice(s) and {outputDeviceCount} audio output device(s) detected. {defaultVoice.Name} ({defaultVoice.Culture}, {defaultVoice.Gender}) is the default voice.");
                }
            }

            return ValueTask.FromResult(status);
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Probing Windows audio output readiness");
            throw new AudioOutputDeviceUnavailableException(
                "Windows could not inspect audio output devices.",
                exception);
        }
    }

    private static DependencyStatus CreateStatus(
        DependencyReadiness readiness,
        string detail) =>
        new(
            "windows.tts",
            "Windows text-to-speech",
            readiness,
            detail);
}