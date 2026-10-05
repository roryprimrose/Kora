using System.Runtime.InteropServices;

using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using NAudio.CoreAudioApi;

namespace Kora.Windows.Session;

internal sealed class WindowsPrivacySource : IWindowsPrivacySource
{
    private readonly IMicrophoneAccessService microphoneAccess;
    private readonly ILogger logger;
    private readonly WindowsSessionNotificationWindow sessionWindow;
    private readonly MMDeviceEnumerator enumerator;
    private readonly MMDeviceNotificationClient notifications;

    public WindowsPrivacySource(IMicrophoneAccessService microphoneAccess, ILogger logger)
    {
        this.microphoneAccess = microphoneAccess;
        this.logger = logger;
        sessionWindow = new WindowsSessionNotificationWindow(OnSessionChanged, logger);
        MMDeviceEnumerator? createdEnumerator = null;
        MMDeviceNotificationClient? createdNotifications = null;
        try
        {
            createdEnumerator = new MMDeviceEnumerator();
            createdNotifications = createdEnumerator.CreateNotificationClient();
            enumerator = createdEnumerator;
            notifications = createdNotifications;
            notifications.DeviceAdded += OnEndpointChanged;
            notifications.DeviceRemoved += OnEndpointChanged;
            notifications.DeviceStateChanged += OnEndpointChanged;
            notifications.DefaultDeviceChanged += OnEndpointChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch
        {
            createdNotifications?.Dispose();
            createdEnumerator?.Dispose();
            sessionWindow.Dispose();
            throw;
        }
    }

    public event EventHandler<WindowsPrivacySignalEventArgs>? Changed;

    public WindowsPrivacySnapshot Read()
    {
        using var queryEnumerator = new MMDeviceEnumerator();
        var microphones = new List<string>();
        try
        {
            var endpoints = queryEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                using (endpoint)
                {
                    microphones.Add(endpoint.ID);
                }
            }
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Observing microphone endpoint topology");
        }

        return new WindowsPrivacySnapshot(
            sessionWindow.IsRegistered ? WindowsSessionNotificationWindow.ReadSessionState() : WindowsSessionState.Unknown,
            microphoneAccess.GetStatus().State,
            0,
            microphones.Order(StringComparer.Ordinal).ToArray(),
            ReadDefault(queryEnumerator, DataFlow.Capture),
            ReadDefault(queryEnumerator, DataFlow.Render));
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        notifications.DeviceAdded -= OnEndpointChanged;
        notifications.DeviceRemoved -= OnEndpointChanged;
        notifications.DeviceStateChanged -= OnEndpointChanged;
        notifications.DefaultDeviceChanged -= OnEndpointChanged;
        notifications.Dispose();
        enumerator.Dispose();
        sessionWindow.Dispose();
    }

    private static string? ReadDefault(MMDeviceEnumerator queryEnumerator, DataFlow flow)
    {
        try
        {
            using var endpoint = queryEnumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return endpoint.State == DeviceState.Active ? endpoint.ID : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void OnEndpointChanged(object? sender, EventArgs eventArgs)
    {
        var reason = WindowsPrivacyChangeReason.DeviceTopology;
        if (eventArgs is DefaultDeviceChangedEventArgs defaultChange)
        {
            if (defaultChange.Role != Role.Multimedia)
            {
                return;
            }

            reason |= defaultChange.Flow switch
            {
                DataFlow.Capture => WindowsPrivacyChangeReason.DefaultMicrophone | WindowsPrivacyChangeReason.InputTopology,
                DataFlow.Render => WindowsPrivacyChangeReason.DefaultSpeaker | WindowsPrivacyChangeReason.OutputTopology,
                _ => WindowsPrivacyChangeReason.Unknown,
            };
        }
        else if (eventArgs is DeviceNotificationEventArgs endpointChange)
        {
            try
            {
                using var queryEnumerator = new MMDeviceEnumerator();
                using var endpoint = queryEnumerator.GetDevice(endpointChange.DeviceId);
                reason |= endpoint.DataFlow switch
                {
                    DataFlow.Capture => WindowsPrivacyChangeReason.InputTopology,
                    DataFlow.Render => WindowsPrivacyChangeReason.OutputTopology,
                    _ => WindowsPrivacyChangeReason.Unknown,
                };
            }
            catch (COMException)
            {
                // Removed/unknown endpoints remain a conservative, unclassified topology change.
            }
        }

        Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(topologyChanged: true, reason: reason));
    }

    private void OnSessionChanged(WindowsSessionState state) =>
        Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(state, reason: WindowsPrivacyChangeReason.Session));

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(
                WindowsSessionState.Suspended, reason: WindowsPrivacyChangeReason.Power));
        }
        else if (eventArgs.Mode == PowerModes.Resume)
        {
            // Unlocked means "requery", not permission to capture or release a run recovery hold.
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(
                WindowsSessionState.Unlocked, reason: WindowsPrivacyChangeReason.Power));
        }
    }
}
