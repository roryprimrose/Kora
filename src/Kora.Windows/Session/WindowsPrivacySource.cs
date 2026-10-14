using System.Runtime.InteropServices;
using System.Text;

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
    private readonly Lock topologySync = new();
    private string? topologyIdentity;

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

    public string? TopologyIdentity
    {
        get
        {
            lock (topologySync)
            {
                return topologyIdentity;
            }
        }
    }

    public WindowsPrivacySnapshot Read()
    {
        using var queryEnumerator = new MMDeviceEnumerator();
        var topology = ReadTopology(queryEnumerator);
        lock (topologySync)
        {
            topologyIdentity = topology.Identity;
        }

        return new WindowsPrivacySnapshot(
            sessionWindow.IsRegistered ? WindowsSessionNotificationWindow.ReadSessionState() : WindowsSessionState.Unknown,
            microphoneAccess.GetStatus().State,
            0,
            topology.Microphones,
            topology.DefaultMicrophoneId,
            topology.DefaultSpeakerId);
    }

    private AudioTopology ReadTopology(MMDeviceEnumerator queryEnumerator)
    {
        var microphones = ReadActiveEndpoints(queryEnumerator, DataFlow.Capture,
            "Observing microphone endpoint topology");
        var speakers = ReadActiveEndpoints(queryEnumerator, DataFlow.Render,
            "Observing output endpoint topology");
        var defaultMicrophoneId = ReadDefault(queryEnumerator, DataFlow.Capture);
        var defaultSpeakerId = ReadDefault(queryEnumerator, DataFlow.Render);
        var identity = CreateTopologyIdentity(microphones, speakers, defaultMicrophoneId, defaultSpeakerId);
        return new AudioTopology(microphones, defaultMicrophoneId, defaultSpeakerId, identity);
    }

    private string[] ReadActiveEndpoints(
        MMDeviceEnumerator queryEnumerator,
        DataFlow flow,
        string failureContext)
    {
        var endpointIds = new List<string>();
        try
        {
            var endpoints = queryEnumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                using (endpoint)
                {
                    endpointIds.Add(endpoint.ID);
                }
            }
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, failureContext);
        }

        return endpointIds.Order(StringComparer.Ordinal).ToArray();
    }

    private static string CreateTopologyIdentity(
        IReadOnlyList<string> microphones,
        IReadOnlyList<string> speakers,
        string? defaultMicrophoneId,
        string? defaultSpeakerId)
    {
        var identity = new StringBuilder();
        Append(identity, defaultMicrophoneId);
        Append(identity, defaultSpeakerId);
        foreach (var microphone in microphones)
        {
            Append(identity, microphone);
        }
        identity.Append('|');
        foreach (var speaker in speakers)
        {
            Append(identity, speaker);
        }
        return identity.ToString();
    }

    private static void Append(StringBuilder identity, string? value)
    {
        identity.Append(value?.Length ?? -1).Append(':').Append(value).Append(';');
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
        var observation = PrivacyObservation.Create();
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

        string identity;
        using (var queryEnumerator = new MMDeviceEnumerator())
        {
            identity = ReadTopology(queryEnumerator).Identity;
        }
        lock (topologySync)
        {
            if (string.Equals(topologyIdentity, identity, StringComparison.Ordinal))
            {
                return;
            }
            topologyIdentity = identity;
        }

        Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(topologyChanged: true, reason: reason,
            observation: observation, topologyIdentity: identity));
    }

    private void OnSessionChanged(WindowsPrivacySignalEventArgs signal) =>
        Changed?.Invoke(this, signal);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        var observation = PrivacyObservation.Create();
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(
                WindowsSessionState.Suspended, reason: WindowsPrivacyChangeReason.Power, observation: observation));
        }
        else if (eventArgs.Mode == PowerModes.Resume)
        {
            // Unlocked means "requery", not permission to capture or release a run recovery hold.
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(
                WindowsSessionState.Unlocked, reason: WindowsPrivacyChangeReason.Power, observation: observation));
        }
    }

    private sealed record AudioTopology(
        string[] Microphones,
        string? DefaultMicrophoneId,
        string? DefaultSpeakerId,
        string Identity);
}
