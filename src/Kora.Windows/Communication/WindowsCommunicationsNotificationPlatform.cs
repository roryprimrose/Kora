using System.Runtime.InteropServices;

using Kora.Core.Communication;
using Kora.Core.Platform;
using Kora.Windows.Session;

using static Kora.Windows.Communication.WindowsCommunicationsActivitySource;

namespace Kora.Windows.Communication;

internal sealed class WindowsCommunicationsNotificationPlatform : IWindowsCommunicationsNotificationPlatform
{
    public IWindowsCommunicationsNotificationLease Open(Action<WindowsCommunicationSignal> publish) => new Lease(publish);

    private sealed class Lease : IWindowsCommunicationsNotificationLease
    {
        private readonly DeviceEnumerator enumerator;
        private readonly Device device;
        private readonly SessionManager manager;
        private readonly Callback callback;
        private readonly string deviceId;
        private bool registered;
        private bool disposed;

        internal Lease(Action<WindowsCommunicationSignal> publish)
        {
            if (WindowsSessionNotificationWindow.ReadSessionState() != WindowsSessionState.Unlocked)
            { throw new InvalidOperationException("Windows session eligibility is unavailable."); }
            enumerator = (DeviceEnumerator)new Enumerator();
            Device? createdDevice = null;
            SessionManager? createdManager = null;
            callback = new(publish);
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 2, out createdDevice));
                Marshal.ThrowExceptionForHR(createdDevice.GetId(out deviceId));
                var id = typeof(SessionManager).GUID;
                Marshal.ThrowExceptionForHR(createdDevice.Activate(in id, 23, 0, out createdManager));
                Marshal.ThrowExceptionForHR(createdManager.RegisterDuckNotification(null, callback));
                registered = true;
                device = createdDevice;
                manager = createdManager;
            }
            catch
            {
                callback.Retire();
                if (createdManager is not null) { Marshal.FinalReleaseComObject(createdManager); }
                if (createdDevice is not null) { Marshal.FinalReleaseComObject(createdDevice); }
                Marshal.FinalReleaseComObject(enumerator);
                throw;
            }
        }

        public Guid DeviceEpoch { get; } = Guid.NewGuid();

        public CommunicationActivityHealth ReadHealth()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (WindowsSessionNotificationWindow.ReadSessionState() != WindowsSessionState.Unlocked)
            { return CommunicationActivityHealth.SessionLost; }
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 2, out var current));
            try
            {
                Marshal.ThrowExceptionForHR(current.GetId(out var id));
                Marshal.ThrowExceptionForHR(current.GetState(out var state));
                if (state != 1 || !string.Equals(id, deviceId, StringComparison.Ordinal))
                { return CommunicationActivityHealth.DeviceLost; }
                Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
                try
                {
                    Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
                    return count is >= 0 and <= 128 ? CommunicationActivityHealth.Healthy : CommunicationActivityHealth.Failed;
                }
                finally { Marshal.FinalReleaseComObject(sessions); }
            }
            finally { Marshal.FinalReleaseComObject(current); }
        }

        public WindowsCommunicationIdentity Resolve(string sessionId)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
            try
            {
                Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
                if (count is < 0 or > 128) { return WindowsCommunicationIdentity.Unknown; }
                for (var index = 0; index < count; index++)
                {
                    Marshal.ThrowExceptionForHR(sessions.GetSession(index, out var unknown));
                    try
                    {
                        if (unknown is not SessionControl control) { return WindowsCommunicationIdentity.Unknown; }
                        Marshal.ThrowExceptionForHR(control.GetSessionInstanceIdentifier(out var id));
                        if (!string.Equals(id, sessionId, StringComparison.Ordinal)) { continue; }
                        var result = control.GetProcessId(out var process);
                        if (result != 0 || process == 0) { return WindowsCommunicationIdentity.Unknown; }
                        return process == Environment.ProcessId ? WindowsCommunicationIdentity.Kora : WindowsCommunicationIdentity.Other;
                    }
                    finally { Marshal.FinalReleaseComObject(unknown); }
                }
                return WindowsCommunicationIdentity.Unknown;
            }
            finally { Marshal.FinalReleaseComObject(sessions); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            callback.Retire();
            if (registered)
            {
                // Preserve all references on failure; retry unregister before releasing the COM owner.
                Marshal.ThrowExceptionForHR(manager.UnregisterDuckNotification(callback));
                registered = false;
            }
            Marshal.FinalReleaseComObject(manager);
            Marshal.FinalReleaseComObject(device);
            Marshal.FinalReleaseComObject(enumerator);
            disposed = true;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class Callback(Action<WindowsCommunicationSignal> publish) : IWindowsCommunicationsDuckNotification
    {
        private readonly Lock gate = new();
        private bool retired;
        internal void Retire() { lock (gate) { retired = true; } }
        public int OnVolumeDuckNotification(string? session, uint count)
        {
            lock (gate)
            {
                if (!retired) { publish(new(WindowsCommunicationSignalKind.Started, session, count)); }
            }
            return 0;
        }
        public int OnVolumeUnduckNotification(string? session)
        {
            lock (gate)
            {
                if (!retired) { publish(new(WindowsCommunicationSignalKind.Ended, session, 0)); }
            }
            return 0;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class Enumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out Device device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Device
    {
        [PreserveSig] int Activate(in Guid id, uint context, nint parameters, out SessionManager manager);
        [PreserveSig] int OpenPropertyStore(uint access, out nint store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface SessionManager
    {
        [PreserveSig] int GetAudioSessionControl(in Guid session, uint flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(in Guid session, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out SessionEnumerator sessions);
        [PreserveSig] int RegisterSessionNotification(nint notification);
        [PreserveSig] int UnregisterSessionNotification(nint notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string? session, IWindowsCommunicationsDuckNotification notification);
        [PreserveSig] int UnregisterDuckNotification(IWindowsCommunicationsDuckNotification notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface SessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface SessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out nint name);
        [PreserveSig] int SetDisplayName(nint name, in Guid context);
        [PreserveSig] int GetIconPath(out nint path);
        [PreserveSig] int SetIconPath(nint path, in Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(in Guid grouping, in Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notification);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notification);
        [PreserveSig] int GetSessionIdentifier(out nint id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint process);
    }

}