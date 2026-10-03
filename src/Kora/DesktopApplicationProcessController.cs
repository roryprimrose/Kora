using System.ComponentModel;
using System.Diagnostics;

using Kora.Core.Platform;

namespace Kora;

internal sealed class DesktopApplicationProcessController : IApplicationProcessController
{
    public void OpenWindowsMicrophonePrivacySettings()
    {
        try
        {
            // Shell URI activation can succeed without exposing the launched process.
            using var process = Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone")
            {
                UseShellExecute = true,
            });
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Windows could not open microphone privacy settings.", exception);
        }
    }

    public void RestartCurrentApplication()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The application executable path is unavailable.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows did not start the replacement application process.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Windows could not restart the application.", exception);
        }
    }
}
