using System.Security;

using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kora.Windows.Audio;

public sealed class WindowsMicrophoneAccessService(
    ILogger<WindowsMicrophoneAccessService> logger) : IMicrophoneAccessService
{
    private const string ConsentPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    private const string DesktopConsentPath = ConsentPath + @"\NonPackaged";
    private const string Allow = "Allow";
    private const string Deny = "Deny";

    public MicrophoneAccessStatus GetStatus()
    {
        try
        {
            var values = new[]
            {
                ReadValue(RegistryHive.LocalMachine, ConsentPath),
                ReadValue(RegistryHive.CurrentUser, ConsentPath),
                ReadValue(RegistryHive.LocalMachine, DesktopConsentPath),
                ReadValue(RegistryHive.CurrentUser, DesktopConsentPath),
                ReadApplicationValue(),
            };

            if (values.Any(value => string.Equals(value, Deny, StringComparison.OrdinalIgnoreCase)))
            {
                return new MicrophoneAccessStatus(
                    MicrophoneAccessState.Denied,
                    "Windows microphone access is blocked for desktop apps. Enable it in Settings > Privacy & security > Microphone.");
            }

            if (values.Any(value => string.Equals(value, Allow, StringComparison.OrdinalIgnoreCase)))
            {
                return new MicrophoneAccessStatus(
                    MicrophoneAccessState.Allowed,
                    "Windows microphone access is allowed for desktop apps.");
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            WindowsLog.Error(logger, exception, "Reading Windows microphone privacy settings");
        }
        catch (SecurityException exception)
        {
            WindowsLog.Error(logger, exception, "Reading Windows microphone privacy settings");
        }
        catch (IOException exception)
        {
            WindowsLog.Error(logger, exception, "Reading Windows microphone privacy settings");
        }

        return new MicrophoneAccessStatus(
            MicrophoneAccessState.Unknown,
            "Windows microphone access could not be confirmed. Check Settings > Privacy & security > Microphone.");
    }

    private static string? ReadApplicationValue()
    {
        var processPath = Environment.ProcessPath;
        return processPath is null
            ? null
            : ReadValue(
                RegistryHive.CurrentUser,
                $@"{DesktopConsentPath}\{processPath.Replace('\\', '#')}");
    }

    private static string? ReadValue(RegistryHive hive, string path)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key = baseKey.OpenSubKey(path);
        return key?.GetValue("Value") as string;
    }
}
