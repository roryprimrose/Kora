using System.ComponentModel;
using System.Diagnostics;

using Kora.Core.Coordination;
using Kora.Core.Platform;

namespace Kora;

internal sealed class DesktopApplicationProcessController(
    IInstanceLifecycleController lifecycle) : IApplicationProcessController
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
        lifecycle.RequestRestartAfterQuiescence();
    }
}
