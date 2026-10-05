using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Session;

using Microsoft.Extensions.Logging.Abstractions;

using NAudio.CoreAudioApi;

namespace Kora.Windows.Audio;

internal static class WindowsCapturePrivacyGate
{
    public static void RequireEligible(MicrophoneDevice microphone)
    {
        // Production recording cannot be authorized by an injected observer or a capability claim.
        if (WindowsSessionNotificationWindow.ReadSessionState() != WindowsSessionState.Unlocked ||
            new WindowsMicrophoneAccessService(NullLogger<WindowsMicrophoneAccessService>.Instance)
                .GetStatus().State != MicrophoneAccessState.Allowed)
        {
            throw new InvalidOperationException(
                "Current Windows session and microphone permission do not permit recording.");
        }

        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = microphone.IsSystemDefault
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(microphone.Id);
        if (endpoint.State != DeviceState.Active || endpoint.DataFlow != DataFlow.Capture)
        {
            throw new ArgumentOutOfRangeException(nameof(microphone), "The selected endpoint is not an active microphone.");
        }
    }
}
